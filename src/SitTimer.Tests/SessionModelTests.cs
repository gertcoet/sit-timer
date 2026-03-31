using SitTimer.Core.Models;

namespace SitTimer.Tests;

[TestFixture]
public class SessionModelTests
{
    // ── IsActive ─────────────────────────────────────────────────────────────

    [Test]
    public void IsActive_WhenEndTimeIsNull_ReturnsTrue()
    {
        var session = new Session { StartTime = DateTime.UtcNow, LastHeartbeat = DateTime.UtcNow };
        Assert.That(session.IsActive, Is.True);
    }

    [Test]
    public void IsActive_WhenEndTimeIsSet_ReturnsFalse()
    {
        var session = new Session
        {
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };
        Assert.That(session.IsActive, Is.False);
    }

    // ── Duration ─────────────────────────────────────────────────────────────

    [Test]
    public void Duration_WhenSessionEnded_ReturnsElapsedTime()
    {
        var start = new DateTime(2024, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2024, 1, 1, 11, 30, 0, DateTimeKind.Utc);
        var session = new Session { StartTime = start, EndTime = end, LastHeartbeat = end };

        Assert.That(session.Duration, Is.EqualTo(TimeSpan.FromHours(2.5)));
    }

    [Test]
    public void Duration_WhenSessionActive_IsPositiveAndApproximate()
    {
        var start = DateTime.UtcNow.AddMinutes(-5);
        var session = new Session { StartTime = start, LastHeartbeat = start };

        // Active sessions use UtcNow, so duration should be ~5 minutes
        Assert.That(session.Duration.TotalMinutes, Is.InRange(4.9, 5.5));
    }

    [Test]
    public void Duration_ZeroDuration_WhenStartAndEndAreSame()
    {
        var time = new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var session = new Session { StartTime = time, EndTime = time, LastHeartbeat = time };

        Assert.That(session.Duration, Is.EqualTo(TimeSpan.Zero));
    }
}
