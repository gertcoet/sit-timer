using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SitTimer.Core.Data;
using SitTimer.Core.Models;
using SitTimer.Core.Services;

namespace SitTimer.Tests;

/// <summary>
/// Each test gets an isolated in-memory SQLite database via a dedicated open connection.
/// SQLite in-memory databases are tied to the connection lifetime, so we keep it open
/// for the duration of each test.
/// </summary>
[TestFixture]
public class SessionServiceTests
{
    private SqliteConnection _connection = null!;
    private TestDbContextFactory _factory = null!;
    private SessionService _sut = null!;

    private class TestDbContextFactory : IDbContextFactory<SitTimerDbContext>
    {
        private readonly DbContextOptions<SitTimerDbContext> _options;
        public TestDbContextFactory(DbContextOptions<SitTimerDbContext> options) => _options = options;
        public SitTimerDbContext CreateDbContext() => new(_options);
    }

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var opts = new DbContextOptionsBuilder<SitTimerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new TestDbContextFactory(opts);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        _sut = new SessionService(_factory, new AppSettings());
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
    }

    /// <summary>Helper to get a fresh context for seeding/asserting in tests.</summary>
    private SitTimerDbContext CreateDb() => _factory.CreateDbContext();

    // ── StartSessionAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task StartSession_ReturnsSessionWithStartTime()
    {
        var before = DateTime.UtcNow;
        var session = await _sut.StartSessionAsync();
        var after = DateTime.UtcNow;

        Assert.That(session.StartTime, Is.InRange(before, after));
    }

    [Test]
    public async Task StartSession_SetsLastHeartbeatToStartTime()
    {
        var session = await _sut.StartSessionAsync();

        Assert.That(session.LastHeartbeat, Is.EqualTo(session.StartTime));
    }

    [Test]
    public async Task StartSession_FirstSession_HasNullBreakTime()
    {
        var session = await _sut.StartSessionAsync();

        Assert.That(session.BreakTime, Is.Null);
    }

    [Test]
    public async Task StartSession_SubsequentSession_BreakTimeIsGapFromPreviousEnd()
    {
        // First session: ended 30 minutes ago
        var firstEnd = DateTime.UtcNow.AddMinutes(-30);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = firstEnd.AddHours(-1),
                EndTime = firstEnd,
                LastHeartbeat = firstEnd
            });
            await db.SaveChangesAsync();
        }

        var second = await _sut.StartSessionAsync();

        // Break time should be ~30 minutes (within a few seconds of test execution)
        Assert.That(second.BreakTime, Is.Not.Null);
        Assert.That(second.BreakTime!.Value.TotalMinutes, Is.InRange(29.9, 30.1));
    }

    [Test]
    public async Task StartSession_IsPersisted()
    {
        await _sut.StartSessionAsync();

        using var db = CreateDb();
        Assert.That(db.Sessions.Count(), Is.EqualTo(1));
    }

    // ── StopActiveSessionAsync ────────────────────────────────────────────────

    [Test]
    public async Task StopActiveSession_SetsEndTime()
    {
        await _sut.StartSessionAsync();
        var before = DateTime.UtcNow;

        var stopped = await _sut.StopActiveSessionAsync();
        var after = DateTime.UtcNow;

        Assert.That(stopped, Is.Not.Null);
        Assert.That(stopped!.EndTime!.Value, Is.InRange(before, after));
    }

    [Test]
    public async Task StopActiveSession_WhenNoActiveSession_ReturnsNull()
    {
        var result = await _sut.StopActiveSessionAsync();

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task StopActiveSession_SessionBecomesInactive()
    {
        await _sut.StartSessionAsync();
        await _sut.StopActiveSessionAsync();

        var active = await _sut.GetActiveSessionAsync();
        Assert.That(active, Is.Null);
    }

    // ── GetActiveSessionAsync ─────────────────────────────────────────────────

    [Test]
    public async Task GetActiveSession_WhenNoSessions_ReturnsNull()
    {
        var result = await _sut.GetActiveSessionAsync();
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetActiveSession_WithActiveSession_ReturnsIt()
    {
        var session = await _sut.StartSessionAsync();

        var active = await _sut.GetActiveSessionAsync();

        Assert.That(active, Is.Not.Null);
        Assert.That(active!.Id, Is.EqualTo(session.Id));
    }

    [Test]
    public async Task GetActiveSession_WithOnlyEndedSessions_ReturnsNull()
    {
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-2),
                EndTime = DateTime.UtcNow.AddHours(-1),
                LastHeartbeat = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetActiveSessionAsync();
        Assert.That(result, Is.Null);
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    [Test]
    public async Task UpdateHeartbeat_UpdatesLastHeartbeatTimestamp()
    {
        var session = await _sut.StartSessionAsync();
        var originalHeartbeat = session.LastHeartbeat;

        await Task.Delay(10); // ensure time advances
        await _sut.UpdateHeartbeatAsync();

        using var db = CreateDb();
        var updated = await db.Sessions.FindAsync(session.Id);
        Assert.That(updated!.LastHeartbeat, Is.GreaterThan(originalHeartbeat));
    }

    [Test]
    public async Task UpdateHeartbeat_WhenNoActiveSession_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(() => _sut.UpdateHeartbeatAsync());
    }

    // ── RecoverOrphanedSessionAsync ───────────────────────────────────────────

    [Test]
    public async Task RecoverOrphanedSession_ClosesOrphanUsingLastHeartbeat()
    {
        var heartbeat = DateTime.UtcNow.AddMinutes(-5);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-1),
                LastHeartbeat = heartbeat
                // EndTime = null → orphan
            });
            await db.SaveChangesAsync();
        }

        await _sut.RecoverOrphanedSessionAsync();

        using (var db = CreateDb())
        {
            var session = await db.Sessions.FirstAsync();
            Assert.That(session.EndTime, Is.EqualTo(heartbeat));
        }
    }

    [Test]
    public async Task RecoverOrphanedSession_WhenNoOrphan_DoesNothing()
    {
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-2),
                EndTime = DateTime.UtcNow.AddHours(-1),
                LastHeartbeat = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        await _sut.RecoverOrphanedSessionAsync(); // should be a no-op

        using (var db = CreateDb())
        {
            var session = await db.Sessions.FirstAsync();
            Assert.That(session.EndTime, Is.Not.Null);
        }
    }

    // ── DeleteSessionAsync ────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSession_RemovesSessionById()
    {
        var session = await _sut.StartSessionAsync();

        await _sut.DeleteSessionAsync(session.Id);

        using var db = CreateDb();
        Assert.That(db.Sessions.Count(), Is.EqualTo(0));
    }

    [Test]
    public async Task DeleteSession_NonExistentId_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(() => _sut.DeleteSessionAsync(99999));
    }

    // ── GetSessionsForDayAsync ────────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForDay_ReturnsOnlyThatDaysSessions()
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = today.ToUniversalTime().AddHours(9), LastHeartbeat = today.ToUniversalTime().AddHours(10), EndTime = today.ToUniversalTime().AddHours(10) },
                new Session { StartTime = yesterday.ToUniversalTime().AddHours(9), LastHeartbeat = yesterday.ToUniversalTime().AddHours(10), EndTime = yesterday.ToUniversalTime().AddHours(10) }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForDayAsync(today);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].StartTime, Is.EqualTo(today.ToUniversalTime().AddHours(9)));
    }

    [Test]
    public async Task GetSessionsForDay_NoSessionsOnDay_ReturnsEmptyList()
    {
        var results = await _sut.GetSessionsForDayAsync(DateTime.Today);
        Assert.That(results, Is.Empty);
    }

    // ── GetSessionsForWeekAsync ───────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForWeek_ReturnsSessionsWithinSevenDays()
    {
        var weekStart = DateTime.Today.AddDays(-(DateTime.Today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)DateTime.Today.DayOfWeek - 1));
        var inWeek = weekStart.ToUniversalTime().AddHours(10);
        var outOfWeek = weekStart.AddDays(-1).ToUniversalTime().AddHours(10);

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = inWeek, LastHeartbeat = inWeek.AddHours(1), EndTime = inWeek.AddHours(1) },
                new Session { StartTime = outOfWeek, LastHeartbeat = outOfWeek.AddHours(1), EndTime = outOfWeek.AddHours(1) }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForWeekAsync(weekStart);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    // ── GetSessionsForMonthAsync ──────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForMonth_ReturnsOnlyCurrentMonthSessions()
    {
        var thisMonth = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var lastMonth = new DateTime(2024, 2, 15, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = thisMonth, LastHeartbeat = thisMonth.AddHours(1), EndTime = thisMonth.AddHours(1) },
                new Session { StartTime = lastMonth, LastHeartbeat = lastMonth.AddHours(1), EndTime = lastMonth.AddHours(1) }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForMonthAsync(2024, 3);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    // For UTC+ timezones, the month end in UTC occurs before local midnight on the last day.
    // Previously GetSessionsForMonthAsync called start.AddMonths(1) on the UTC time, which cut
    // the query short by the UTC offset (e.g. March 28 22:00 UTC for UTC+2 instead of March 31 22:00 UTC).
    private static readonly TimeZoneInfo Utc2 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

    [Test]
    public async Task GetSessionsForMonth_UtcPlusTimezone_IncludesLastDayOfMonth()
    {
        // March 31 at 10:00 local (UTC+2) = March 31 at 08:00 UTC
        var lastDay = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 3, 31, 10, 0, 0), Utc2);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session { StartTime = lastDay, LastHeartbeat = lastDay.AddHours(1), EndTime = lastDay.AddHours(1) });
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForMonthAsync(2024, 3, Utc2);

        Assert.That(results, Has.Count.EqualTo(1), "Session on last day of month must be included");
    }

    [Test]
    public async Task GetSessionsForMonth_UtcPlusTimezone_ExcludesFirstDayOfNextMonth()
    {
        // April 1 at 00:30 local (UTC+2) = March 31 at 22:30 UTC
        var firstOfNextMonth = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 4, 1, 0, 30, 0), Utc2);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session { StartTime = firstOfNextMonth, LastHeartbeat = firstOfNextMonth.AddHours(1), EndTime = firstOfNextMonth.AddHours(1) });
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForMonthAsync(2024, 3, Utc2);

        Assert.That(results, Is.Empty, "Session on first day of next month must be excluded");
    }

    // ── GetSessionsForYearAsync ───────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForYear_ReturnsOnlyThatYearsSessions()
    {
        var thisYear = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var lastYear = new DateTime(2023, 6, 1, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = thisYear, LastHeartbeat = thisYear.AddHours(1), EndTime = thisYear.AddHours(1) },
                new Session { StartTime = lastYear, LastHeartbeat = lastYear.AddHours(1), EndTime = lastYear.AddHours(1) }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForYearAsync(2024);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetSessionsForYear_UtcPlusTimezone_IncludesLastDayOfYear()
    {
        // Dec 31 at 10:00 local (UTC+2) = Dec 31 at 08:00 UTC
        var lastDay = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2024, 12, 31, 10, 0, 0), Utc2);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session { StartTime = lastDay, LastHeartbeat = lastDay.AddHours(1), EndTime = lastDay.AddHours(1) });
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForYearAsync(2024, Utc2);

        Assert.That(results, Has.Count.EqualTo(1), "Session on Dec 31 must be included in the year query");
    }

    // ── TotalDuration (static) ────────────────────────────────────────────────

    [Test]
    public void TotalDuration_EmptyList_ReturnsZero()
    {
        var result = SessionService.TotalDuration([]);
        Assert.That(result, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TotalDuration_SumsDurationsCorrectly()
    {
        var t = DateTime.UtcNow;
        var sessions = new[]
        {
            new Session { StartTime = t, EndTime = t.AddHours(1), LastHeartbeat = t },
            new Session { StartTime = t.AddHours(2), EndTime = t.AddHours(3.5), LastHeartbeat = t }
        };

        var result = SessionService.TotalDuration(sessions);

        Assert.That(result, Is.EqualTo(TimeSpan.FromHours(2.5)));
    }

    // ── GroupByDay (static) ───────────────────────────────────────────────────

    [Test]
    public void GroupByDay_GroupsSessionsByLocalDate()
    {
        var day1 = new DateTime(2024, 1, 10, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var day2 = new DateTime(2024, 1, 11, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();

        var sessions = new[]
        {
            new Session { StartTime = day1, EndTime = day1.AddHours(2), LastHeartbeat = day1 },
            new Session { StartTime = day1.AddHours(4), EndTime = day1.AddHours(6), LastHeartbeat = day1 },
            new Session { StartTime = day2, EndTime = day2.AddHours(3), LastHeartbeat = day2 }
        };

        var result = SessionService.GroupByDay(sessions);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[new DateTime(2024, 1, 10)], Is.EqualTo(4.0).Within(0.00001));
        Assert.That(result[new DateTime(2024, 1, 11)], Is.EqualTo(3.0).Within(0.00001));
    }

    // ── GroupByMonth (static) ─────────────────────────────────────────────────

    [Test]
    public void GroupByMonth_GroupsSessionsByLocalMonth()
    {
        var jan = new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var feb = new DateTime(2024, 2, 10, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();

        var sessions = new[]
        {
            new Session { StartTime = jan, EndTime = jan.AddHours(2), LastHeartbeat = jan },
            new Session { StartTime = feb, EndTime = feb.AddHours(3), LastHeartbeat = feb }
        };

        var result = SessionService.GroupByMonth(sessions);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[new DateTime(2024, 1, 1)], Is.EqualTo(2.0).Within(0.00001));
        Assert.That(result[new DateTime(2024, 2, 1)], Is.EqualTo(3.0).Within(0.00001));
    }

    // ── Dashboard data-flow (replicates ViewModel bucket logic) ────────────────

    /// <summary>
    /// Replicates the Monday calculation from DashboardViewModel.LoadWeekAsync
    /// to verify it produces the correct week start for various days.
    /// </summary>
    private static DateTime CalculateMonday(DateTime selectedDate)
    {
        var monday = selectedDate.AddDays(-(int)selectedDate.DayOfWeek + (int)DayOfWeek.Monday);
        if (selectedDate.DayOfWeek == DayOfWeek.Sunday) monday = monday.AddDays(-7);
        return monday;
    }

    [TestCase(2024, 3, 11, ExpectedResult = "2024-03-11")] // Monday
    [TestCase(2024, 3, 13, ExpectedResult = "2024-03-11")] // Wednesday
    [TestCase(2024, 3, 16, ExpectedResult = "2024-03-11")] // Saturday
    [TestCase(2024, 3, 17, ExpectedResult = "2024-03-11")] // Sunday
    [TestCase(2024, 1, 1,  ExpectedResult = "2024-01-01")] // Monday (New Year)
    public string Week_MondayCalculation_ReturnsCorrectMonday(int y, int m, int d)
    {
        var monday = CalculateMonday(new DateTime(y, m, d));
        return monday.ToString("yyyy-MM-dd");
    }

    [Test]
    public async Task Week_BucketLookup_MatchesGroupByDayKeys()
    {
        // Seed sessions on Monday and Friday of a known week
        var monday = new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Local);
        var mondaySession = monday.AddHours(9).ToUniversalTime();
        var fridaySession = monday.AddDays(4).AddHours(10).ToUniversalTime();

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = mondaySession, EndTime = mondaySession.AddHours(2), LastHeartbeat = mondaySession },
                new Session { StartTime = fridaySession, EndTime = fridaySession.AddHours(3), LastHeartbeat = fridaySession }
            );
            await db.SaveChangesAsync();
        }

        var sessions = await _sut.GetSessionsForWeekAsync(monday);
        var byDay = SessionService.GroupByDay(sessions);

        // Replicate ViewModel bucket logic
        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToArray();
        var values = days.Select(d => byDay.TryGetValue(d, out var h) ? h : 0.0).ToArray();

        Assert.That(values[0], Is.EqualTo(2.0).Within(0.01), "Monday should have 2 hours");
        Assert.That(values[4], Is.EqualTo(3.0).Within(0.01), "Friday should have 3 hours");
        Assert.That(values[1], Is.EqualTo(0.0), "Tuesday should be zero");
    }

    [Test]
    public async Task Month_BucketLookup_MatchesGroupByDayKeys()
    {
        // Seed sessions on day 1 and day 15 of March 2024
        var day1 = new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var day15 = new DateTime(2024, 3, 15, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = day1, EndTime = day1.AddHours(1), LastHeartbeat = day1 },
                new Session { StartTime = day15, EndTime = day15.AddHours(4), LastHeartbeat = day15 }
            );
            await db.SaveChangesAsync();
        }

        var sessions = await _sut.GetSessionsForMonthAsync(2024, 3);
        var byDay = SessionService.GroupByDay(sessions);

        // Replicate ViewModel bucket logic
        var selectedDate = new DateTime(2024, 3, 15);
        var daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
        var days = Enumerable.Range(1, daysInMonth)
            .Select(d => new DateTime(selectedDate.Year, selectedDate.Month, d))
            .ToArray();
        var values = days.Select(d => byDay.TryGetValue(d, out var h) ? h : 0.0).ToArray();

        Assert.That(values[0], Is.EqualTo(1.0).Within(0.01), "Day 1 should have 1 hour");
        Assert.That(values[14], Is.EqualTo(4.0).Within(0.01), "Day 15 should have 4 hours");
        Assert.That(values.Length, Is.EqualTo(31), "March has 31 days");
    }

    [Test]
    public async Task Year_BucketLookup_MatchesGroupByMonthKeys()
    {
        var jan = new DateTime(2024, 1, 10, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var jun = new DateTime(2024, 6, 20, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();

        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = jan, EndTime = jan.AddHours(2), LastHeartbeat = jan },
                new Session { StartTime = jun, EndTime = jun.AddHours(5), LastHeartbeat = jun }
            );
            await db.SaveChangesAsync();
        }

        var sessions = await _sut.GetSessionsForYearAsync(2024);
        var byMonth = SessionService.GroupByMonth(sessions);

        // Replicate ViewModel bucket logic
        var months = Enumerable.Range(1, 12)
            .Select(m => new DateTime(2024, m, 1))
            .ToArray();
        var values = months.Select(m => byMonth.TryGetValue(m, out var h) ? h : 0.0).ToArray();

        Assert.That(values[0], Is.EqualTo(2.0).Within(0.01), "January should have 2 hours");
        Assert.That(values[5], Is.EqualTo(5.0).Within(0.01), "June should have 5 hours");
        Assert.That(values[2], Is.EqualTo(0.0), "March should be zero");
        Assert.That(values.Length, Is.EqualTo(12));
    }

    // ── BackfillBreakTimesAsync ───────────────────────────────────────────────

    [Test]
    public async Task BackfillBreakTimes_FillsMissingBreakTimes()
    {
        using (var db = CreateDb())
        {
            var first = new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-3),
                EndTime = DateTime.UtcNow.AddHours(-2),
                LastHeartbeat = DateTime.UtcNow.AddHours(-2),
                BreakTime = null
            };
            var second = new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow.AddMinutes(-30),
                LastHeartbeat = DateTime.UtcNow.AddMinutes(-30),
                BreakTime = null // gap of ~1 hour from first.EndTime
            };
            db.Sessions.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        await _sut.BackfillBreakTimesAsync();

        using (var db = CreateDb())
        {
            var sessions = await db.Sessions.OrderBy(s => s.StartTime).ToListAsync();
            var second = sessions[1];
            Assert.That(second.BreakTime, Is.Not.Null);
            Assert.That(second.BreakTime!.Value.TotalMinutes, Is.InRange(59.0, 61.0));
        }
    }

    [Test]
    public async Task BackfillBreakTimes_SkipsSessionsThatAlreadyHaveBreakTime()
    {
        var existingBreak = TimeSpan.FromMinutes(42);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                BreakTime = existingBreak
            });
            await db.SaveChangesAsync();
        }

        await _sut.BackfillBreakTimesAsync();

        using (var db = CreateDb())
        {
            var session = await db.Sessions.FirstAsync();
            Assert.That(session.BreakTime, Is.EqualTo(existingBreak));
        }
    }

    // ── Cross-midnight merge ──────────────────────────────────────────────────

    [Test]
    public async Task StartSession_CrossMidnight_MergesWhenBreakIsShort()
    {
        // Simulate a session that ended just before midnight (1 minute ago)
        var recentEnd = DateTime.UtcNow.AddMinutes(-1);
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session
            {
                StartTime = recentEnd.AddHours(-2),
                EndTime = recentEnd,
                LastHeartbeat = recentEnd
            });
            await db.SaveChangesAsync();
        }

        // Default MinBreakMinutes is 2, so a 1-minute break should merge
        var session = await _sut.StartSessionAsync();

        // Should have reopened the previous session (EndTime cleared)
        Assert.That(session.EndTime, Is.Null);
        using var dbAssert = CreateDb();
        Assert.That(dbAssert.Sessions.Count(), Is.EqualTo(1), "Should not create a new session");
    }

    // ── GetSessionsForRangeAsync ─────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForRange_ReturnsOnlySessionsInRange()
    {
        var tz = TimeZoneInfo.Utc;
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = new DateTime(2025, 3, 10, 10, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 3, 10, 11, 0, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = new DateTime(2025, 3, 12, 10, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 3, 12, 11, 0, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = new DateTime(2025, 3, 15, 10, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 3, 15, 11, 0, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForRangeAsync(new DateTime(2025, 3, 11), new DateTime(2025, 3, 13), tz);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].StartTime.Day, Is.EqualTo(12));
    }

    [Test]
    public async Task GetSessionsForRange_InclusiveEndDate()
    {
        var tz = TimeZoneInfo.Utc;
        using (var db = CreateDb())
        {
            db.Sessions.Add(new Session { StartTime = new DateTime(2025, 3, 15, 23, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 3, 15, 23, 30, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetSessionsForRangeAsync(new DateTime(2025, 3, 15), new DateTime(2025, 3, 15), tz);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    // ── GetAllSessionsAsync ──────────────────────────────────────────────────

    [Test]
    public async Task GetAllSessions_ReturnsEverythingOrdered()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 6, 1, 11, 0, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc), LastHeartbeat = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        var results = await _sut.GetAllSessionsAsync();

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].StartTime, Is.LessThan(results[1].StartTime));
    }

    // ── MergeSessionsAsync ──────────────────────────────────────────────────

    [Test]
    public async Task MergeSessions_AssignsSharedMergeGroupId()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-3), EndTime = DateTime.UtcNow.AddHours(-2), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();
        var groupId = await _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id });

        using (var db = CreateDb())
        {
            var merged = db.Sessions.Where(s => s.MergeGroupId == groupId).ToList();
            var unmerged = db.Sessions.Where(s => s.MergeGroupId == null).ToList();
            Assert.That(merged, Has.Count.EqualTo(2));
            Assert.That(unmerged, Has.Count.EqualTo(1));
            Assert.That(merged[0].MergeGroupId, Is.EqualTo(merged[1].MergeGroupId));
        }
    }

    [Test]
    public void MergeSessions_LessThanTwo_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.MergeSessionsAsync(new[] { 1 }));
    }

    [Test]
    public async Task MergeSessions_AlreadyMergedSession_Throws()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-3), EndTime = DateTime.UtcNow.AddHours(-2), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();
        await _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id });

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.MergeSessionsAsync(new[] { all[0].Id, all[2].Id }));
    }

    [Test]
    public async Task MergeSessions_ActiveSession_Throws()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = null, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id }));
    }

    // ── UnmergeSessionsAsync ────────────────────────────────────────────────

    [Test]
    public async Task UnmergeSessions_ClearsMergeGroupId()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();
        var groupId = await _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id });
        await _sut.UnmergeSessionsAsync(groupId);

        using (var db = CreateDb())
        {
            var sessions = db.Sessions.ToList();
            Assert.That(sessions, Has.All.Property(nameof(Session.MergeGroupId)).Null);
        }
    }

    [Test]
    public async Task UnmergeSessions_NonExistentGroupId_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(() =>
            _sut.UnmergeSessionsAsync(Guid.NewGuid().ToString()));
    }

    // ── DeleteSessionAsync merge guard ──────────────────────────────────────

    [Test]
    public async Task DeleteSession_MergedSession_Throws()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();
        await _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id });

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DeleteSessionAsync(all[0].Id));
    }

    [Test]
    public async Task DeleteSession_AfterUnmerge_Succeeds()
    {
        using (var db = CreateDb())
        {
            db.Sessions.AddRange(
                new Session { StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1), LastHeartbeat = DateTime.UtcNow },
                new Session { StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var all = await _sut.GetAllSessionsAsync();
        var groupId = await _sut.MergeSessionsAsync(new[] { all[0].Id, all[1].Id });
        await _sut.UnmergeSessionsAsync(groupId);

        Assert.DoesNotThrowAsync(() => _sut.DeleteSessionAsync(all[0].Id));

        using (var db = CreateDb())
        {
            Assert.That(db.Sessions.Count(), Is.EqualTo(1));
        }
    }
}
