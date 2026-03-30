using System.Windows.Forms;

namespace SitTimer.App.Notifications;

/// <summary>
/// Shows Windows balloon-tip notifications via NotifyIcon.
/// The NotifyIcon instance is provided by TrayManager.
/// </summary>
public class NotificationService
{
    private NotifyIcon? _notifyIcon;

    public void Attach(NotifyIcon icon) => _notifyIcon = icon;

    public void NotifyStarted(DateTime startTime)
    {
        var local = startTime.ToLocalTime();
        Show("Sit timer started", $"Started at {local:HH:mm}", ToolTipIcon.Info);
    }

    public void NotifyStopped(TimeSpan duration)
    {
        Show("Sit timer stopped", $"Session: {FormatDuration(duration)}", ToolTipIcon.Info);
    }

    private void Show(string title, string message, ToolTipIcon icon)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m";
}
