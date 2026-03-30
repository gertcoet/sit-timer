namespace SitTimer.Core.Models;

public class Session
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime LastHeartbeat { get; set; }

    public TimeSpan Duration =>
        (EndTime ?? DateTime.UtcNow) - StartTime;

    public bool IsActive => EndTime is null;
}
