using Microsoft.EntityFrameworkCore;
using SitTimer.Core.Data;
using SitTimer.Core.Models;

namespace SitTimer.Core.Services;

public class SessionService
{
    private readonly SitTimerDbContext _db;

    public SessionService(SitTimerDbContext db) => _db = db;

    // ── Session lifecycle ────────────────────────────────────────────────────

    public async Task<Session> StartSessionAsync()
    {
        var now = DateTime.UtcNow;
        var localToday = DateTime.Now.Date;
        var todayUtcStart = localToday.ToUniversalTime();
        var todayUtcEnd = localToday.AddDays(1).ToUniversalTime();

        var previous = await _db.Sessions
            .Where(s => s.EndTime != null && s.StartTime >= todayUtcStart && s.StartTime < todayUtcEnd)
            .OrderByDescending(s => s.EndTime)
            .FirstOrDefaultAsync();

        var session = new Session
        {
            StartTime = now,
            LastHeartbeat = now,
            BreakTime = previous is not null ? now - previous.EndTime!.Value : null
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task<Session?> StopActiveSessionAsync()
    {
        var session = await GetActiveSessionAsync();
        if (session is null) return null;

        session.EndTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task UpdateHeartbeatAsync()
    {
        var session = await GetActiveSessionAsync();
        if (session is null) return;

        session.LastHeartbeat = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<Session?> GetActiveSessionAsync() =>
        await _db.Sessions.FirstOrDefaultAsync(s => s.EndTime == null);

    public async Task BackfillBreakTimesAsync()
    {
        var sessions = await _db.Sessions
            .Where(s => s.EndTime != null && s.BreakTime == null)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        foreach (var session in sessions)
        {
            var previous = await _db.Sessions
                .Where(s => s.EndTime != null && s.EndTime < session.StartTime)
                .OrderByDescending(s => s.EndTime)
                .FirstOrDefaultAsync();

            if (previous is not null)
                session.BreakTime = session.StartTime - previous.EndTime!.Value;
        }

        await _db.SaveChangesAsync();
    }

    // ── Crash recovery ───────────────────────────────────────────────────────

    /// <summary>
    /// If an orphaned (no EndTime) session exists from a previous run,
    /// close it using its last known heartbeat timestamp.
    /// </summary>
    public async Task RecoverOrphanedSessionAsync()
    {
        var orphan = await _db.Sessions
            .Where(s => s.EndTime == null)
            .FirstOrDefaultAsync();

        if (orphan is not null)
        {
            orphan.EndTime = orphan.LastHeartbeat;
            await _db.SaveChangesAsync();
        }
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public async Task<List<Session>> GetSessionsForDayAsync(DateTime localDate)
    {
        var start = localDate.Date.ToUniversalTime();
        var end = start.AddDays(1);
        return await _db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForWeekAsync(DateTime localWeekStart)
    {
        var start = localWeekStart.Date.ToUniversalTime();
        var end = start.AddDays(7);
        return await _db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForMonthAsync(int year, int month, TimeZoneInfo? tz = null)
    {
        tz ??= TimeZoneInfo.Local;
        var startLocal = new DateTime(year, month, 1);
        var start = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var end   = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddMonths(1), tz);
        return await _db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<List<Session>> GetSessionsForYearAsync(int year, TimeZoneInfo? tz = null)
    {
        tz ??= TimeZoneInfo.Local;
        var startLocal = new DateTime(year, 1, 1);
        var start = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var end   = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddYears(1), tz);
        return await _db.Sessions
            .Where(s => s.StartTime >= start && s.StartTime < end)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
    }

    public async Task DeleteSessionAsync(int id)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session is not null)
        {
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync();
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
