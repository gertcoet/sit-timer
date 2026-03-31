using Microsoft.Data.Sqlite;
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
    private SitTimerDbContext _db = null!;
    private SessionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var opts = new DbContextOptionsBuilder<SitTimerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SitTimerDbContext(opts);
        _db.Database.EnsureCreated();
        _sut = new SessionService(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

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
        _db.Sessions.Add(new Session
        {
            StartTime = firstEnd.AddHours(-1),
            EndTime = firstEnd,
            LastHeartbeat = firstEnd
        });
        await _db.SaveChangesAsync();

        var second = await _sut.StartSessionAsync();

        // Break time should be ~30 minutes (within a few seconds of test execution)
        Assert.That(second.BreakTime, Is.Not.Null);
        Assert.That(second.BreakTime!.Value.TotalMinutes, Is.InRange(29.9, 30.1));
    }

    [Test]
    public async Task StartSession_IsPersisted()
    {
        await _sut.StartSessionAsync();

        Assert.That(_db.Sessions.Count(), Is.EqualTo(1));
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
        _db.Sessions.Add(new Session
        {
            StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = DateTime.UtcNow.AddHours(-1),
            LastHeartbeat = DateTime.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

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

        await _db.Entry(session).ReloadAsync();
        Assert.That(session.LastHeartbeat, Is.GreaterThan(originalHeartbeat));
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
        _db.Sessions.Add(new Session
        {
            StartTime = DateTime.UtcNow.AddHours(-1),
            LastHeartbeat = heartbeat
            // EndTime = null → orphan
        });
        await _db.SaveChangesAsync();

        await _sut.RecoverOrphanedSessionAsync();

        var session = await _db.Sessions.FirstAsync();
        Assert.That(session.EndTime, Is.EqualTo(heartbeat));
    }

    [Test]
    public async Task RecoverOrphanedSession_WhenNoOrphan_DoesNothing()
    {
        _db.Sessions.Add(new Session
        {
            StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = DateTime.UtcNow.AddHours(-1),
            LastHeartbeat = DateTime.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

        await _sut.RecoverOrphanedSessionAsync(); // should be a no-op

        var session = await _db.Sessions.FirstAsync();
        Assert.That(session.EndTime, Is.Not.Null);
    }

    // ── DeleteSessionAsync ────────────────────────────────────────────────────

    [Test]
    public async Task DeleteSession_RemovesSessionById()
    {
        var session = await _sut.StartSessionAsync();

        await _sut.DeleteSessionAsync(session.Id);

        Assert.That(_db.Sessions.Count(), Is.EqualTo(0));
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

        _db.Sessions.AddRange(
            new Session { StartTime = today.ToUniversalTime().AddHours(9), LastHeartbeat = today.ToUniversalTime().AddHours(10), EndTime = today.ToUniversalTime().AddHours(10) },
            new Session { StartTime = yesterday.ToUniversalTime().AddHours(9), LastHeartbeat = yesterday.ToUniversalTime().AddHours(10), EndTime = yesterday.ToUniversalTime().AddHours(10) }
        );
        await _db.SaveChangesAsync();

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

        _db.Sessions.AddRange(
            new Session { StartTime = inWeek, LastHeartbeat = inWeek.AddHours(1), EndTime = inWeek.AddHours(1) },
            new Session { StartTime = outOfWeek, LastHeartbeat = outOfWeek.AddHours(1), EndTime = outOfWeek.AddHours(1) }
        );
        await _db.SaveChangesAsync();

        var results = await _sut.GetSessionsForWeekAsync(weekStart);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    // ── GetSessionsForMonthAsync ──────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForMonth_ReturnsOnlyCurrentMonthSessions()
    {
        var thisMonth = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var lastMonth = new DateTime(2024, 2, 15, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();

        _db.Sessions.AddRange(
            new Session { StartTime = thisMonth, LastHeartbeat = thisMonth.AddHours(1), EndTime = thisMonth.AddHours(1) },
            new Session { StartTime = lastMonth, LastHeartbeat = lastMonth.AddHours(1), EndTime = lastMonth.AddHours(1) }
        );
        await _db.SaveChangesAsync();

        var results = await _sut.GetSessionsForMonthAsync(2024, 3);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    // ── GetSessionsForYearAsync ───────────────────────────────────────────────

    [Test]
    public async Task GetSessionsForYear_ReturnsOnlyThatYearsSessions()
    {
        var thisYear = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var lastYear = new DateTime(2023, 6, 1, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();

        _db.Sessions.AddRange(
            new Session { StartTime = thisYear, LastHeartbeat = thisYear.AddHours(1), EndTime = thisYear.AddHours(1) },
            new Session { StartTime = lastYear, LastHeartbeat = lastYear.AddHours(1), EndTime = lastYear.AddHours(1) }
        );
        await _db.SaveChangesAsync();

        var results = await _sut.GetSessionsForYearAsync(2024);

        Assert.That(results, Has.Count.EqualTo(1));
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

    // ── BackfillBreakTimesAsync ───────────────────────────────────────────────

    [Test]
    public async Task BackfillBreakTimes_FillsMissingBreakTimes()
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
        _db.Sessions.AddRange(first, second);
        await _db.SaveChangesAsync();

        await _sut.BackfillBreakTimesAsync();

        await _db.Entry(second).ReloadAsync();
        Assert.That(second.BreakTime, Is.Not.Null);
        Assert.That(second.BreakTime!.Value.TotalMinutes, Is.InRange(59.0, 61.0));
    }

    [Test]
    public async Task BackfillBreakTimes_SkipsSessionsThatAlreadyHaveBreakTime()
    {
        var existingBreak = TimeSpan.FromMinutes(42);
        _db.Sessions.Add(new Session
        {
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            BreakTime = existingBreak
        });
        await _db.SaveChangesAsync();

        await _sut.BackfillBreakTimesAsync();

        var session = await _db.Sessions.FirstAsync();
        Assert.That(session.BreakTime, Is.EqualTo(existingBreak));
    }
}
