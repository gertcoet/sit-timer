using Microsoft.EntityFrameworkCore;
using SitTimer.Core.Data;
using SitTimer.Core.Models;

namespace SitTimer.Core.Services;

public class SessionService
{
    private readonly IDbContextFactory<SitTimerDbContext> _dbFactory;
    private readonly AppSettings _settings;

    public SessionService(IDbContextFactory<SitTimerDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    // ── Session lifecycle ────────────────────────────────────────────────────

    public async Task<Session> StartSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        var previous = await db.Sessions
            .Where(s => s.EndTime != null)
            .OrderByDescending(s => s.EndTime)
            .FirstOrDefaultAsync();

        if (previous is not null)
        {
            var breakDuration = now - previous.EndTime!.Value;
            if (breakDuration.TotalMinutes < _settings.MinBreakMinutes)
            {
                previous.EndTime = null;
                previous.LastHeartbeat = now;
                await db.SaveChangesAsync();
                return previous;
            }
        }

        var session = new Session
        {
            StartTime = now,
            LastHeartbeat = now,
            BreakTime = previous is not null ? now - previous.EndTime!.Value : null
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    public async Task<Session?> StopActiveSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.EndTime == null);
        if (session is null) return null;

        session.EndTime = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return session;
    }

    public async Task UpdateHeartbeatAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.EndTime == null);
        if (session is null) return;

        session.LastHeartbeat = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<Session?> GetActiveSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions.FirstOrDefaultAsync(s => s.EndTime == null);
    }

    public async Task BackfillBreakTimesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sessions = await db.Sessions
            .Where(s => s.EndTime != null && s.BreakTime == null)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        foreach (var session in sessions)
        {
            var previous = await db.Sessions
                .Where(s => s.EndTime != null && s.EndTime < session.StartTime)
                .OrderByDescending(s => s.EndTime)
                .FirstOrDefaultAsync();

            if (previous is not null)
                session.BreakTime = session.StartTime - previous.EndTime!.Value;
        }

        await db.SaveChangesAsync();
    }

    // ── Crash recovery ───────────────────────────────────────────────────────

    /// <summary>
    /// If an orphaned (no EndTime) session exists from a previous run,
    /// close it using its last known heartbeat timestamp.
    /// </summary>
    public async Task RecoverOrphanedSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var orphan = await db.Sessions
            .Where(s => s.EndTime == null)
            .FirstOrDefaultAsync();

        if (orphan is not null)
        {
            orphan.EndTime = orphan.LastHeartbeat;
            await db.SaveChangesAsync();
        }
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<List<Session>> GetSessionsForDayAsync(DateTime localDate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var start = localDate.Date.ToUniversalTime();
        var end = start.AddDays(1);
        return await db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForWeekAsync(DateTime localWeekStart)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var start = localWeekStart.Date.ToUniversalTime();
        var end = start.AddDays(7);
        return await db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForMonthAsync(int year, int month, TimeZoneInfo? tz = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        tz ??= TimeZoneInfo.Local;
        var startLocal = new DateTime(year, month, 1);
        var start = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var end   = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddMonths(1), tz);
        return await db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForYearAsync(int year, TimeZoneInfo? tz = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        tz ??= TimeZoneInfo.Local;
        var startLocal = new DateTime(year, 1, 1);
        var start = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var end   = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddYears(1), tz);
        return await db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task DeleteSessionAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions.FindAsync(id);
        if (session is not null)
        {
            db.Sessions.Remove(session);
            await db.SaveChangesAsync();
        }
    }

    // ── Aggregation helpers ──────────────────────────────────────────────────

    /// <summary>Returns total sitting duration for a list of sessions.</summary>
    public static TimeSpan TotalDuration(IEnumerable<Session> sessions) =>
        sessions.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);

    /// <summary>
    /// Groups sessions by local day and returns total hours per day.
    /// Key = local date (midnight), Value = total hours.
    /// </summary>
    public static Dictionary<DateTime, double> GroupByDay(IEnumerable<Session> sessions) =>
        sessions
            .GroupBy(s => s.StartTime.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => TotalDuration(g).TotalHours);

    /// <summary>Groups sessions by local month. Key = first day of month.</summary>
    public static Dictionary<DateTime, double> GroupByMonth(IEnumerable<Session> sessions) =>
        sessions
            .GroupBy(s => new DateTime(
                s.StartTime.ToLocalTime().Year,
                s.StartTime.ToLocalTime().Month, 1))
            .ToDictionary(g => g.Key, g => TotalDuration(g).TotalHours);
}
