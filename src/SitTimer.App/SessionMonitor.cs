using Microsoft.Win32;
using SitTimer.App.Notifications;
using SitTimer.App.TrayIcon;
using SitTimer.Core.Services;

namespace SitTimer.App;

/// <summary>
/// Wires Windows session/power events to SessionService and manages the heartbeat,
/// tooltip, and sitting-reminder timers.
/// </summary>
public class SessionMonitor : IDisposable
{
    private readonly SessionService _sessionService;
    private readonly NotificationService _notifications;
    private readonly TrayManager _tray;
    private readonly AppSettings _settings;
    private readonly System.Timers.Timer _heartbeatTimer;
    private readonly System.Timers.Timer _tooltipTimer;
    private readonly System.Timers.Timer _reminderTimer;
    private bool _disposed;

    public SessionMonitor(
        SessionService sessionService,
        NotificationService notifications,
        TrayManager tray,
        AppSettings settings)
    {
        _sessionService = sessionService;
        _notifications = notifications;
        _tray = tray;
        _settings = settings;

        _heartbeatTimer = new System.Timers.Timer(60_000);
        _heartbeatTimer.Elapsed += async (_, _) => await _sessionService.UpdateHeartbeatAsync();

        _tooltipTimer = new System.Timers.Timer(30_000);
        _tooltipTimer.Elapsed += async (_, _) => await UpdateTooltipAsync();

        _reminderTimer = new System.Timers.Timer(ReminderIntervalMs()) { AutoReset = true };
        _reminderTimer.Elapsed += async (_, _) => await OnReminderTickAsync();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Called on app startup — recover crash, then start a session if PC is unlocked.</summary>
    public async Task InitialiseAsync()
    {
        await _sessionService.RecoverOrphanedSessionAsync();

        var active = await _sessionService.GetActiveSessionAsync();
        if (active is null)
            await StartSessionAsync();

        _tooltipTimer.Start();
        await UpdateTooltipAsync();
    }

    public bool IsSessionActive =>
        _sessionService.GetActiveSessionAsync().GetAwaiter().GetResult() is not null;

    public async Task ManualStartAsync()
    {
        if (IsSessionActive) return;
        await StartSessionAsync();
    }

    public async Task ManualStopAsync()
    {
        if (!IsSessionActive) return;
        await StopSessionAsync();
    }

    /// <summary>
    /// Updates the reminder interval. Restarts the reminder timer immediately if a
    /// session is currently active so the new interval takes effect right away.
    /// </summary>
    public void UpdateReminderInterval(int minutes)
    {
        _settings.ReminderMinutes = minutes;

        if (minutes <= 0)
        {
            _reminderTimer.Stop();
            return;
        }

        _reminderTimer.Interval = ReminderIntervalMs();

        if (IsSessionActive)
        {
            _reminderTimer.Stop();
            _reminderTimer.Start();
        }
    }

    // ── Windows events ───────────────────────────────────────────────────────

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
                _ = StartSessionAsync();
                break;

            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
                _ = StopSessionAsync();
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                _ = StartSessionAsync();
                break;
            case PowerModes.Suspend:
                _ = StopSessionAsync();
                break;
            case PowerModes.StatusChange:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    // ── Session helpers ──────────────────────────────────────────────────────

    private async Task StartSessionAsync()
    {
        var active = await _sessionService.GetActiveSessionAsync();
        if (active is not null) return;

        var session = await _sessionService.StartSessionAsync();
        _heartbeatTimer.Start();
        if (_settings.ReminderMinutes > 0)
        {
            _reminderTimer.Interval = ReminderIntervalMs();
            _reminderTimer.Start();
        }
        _notifications.NotifyStarted(session.StartTime);
        await UpdateTooltipAsync();
    }

    private async Task StopSessionAsync()
    {
        _heartbeatTimer.Stop();
        _reminderTimer.Stop();
        var session = await _sessionService.StopActiveSessionAsync();
        if (session is not null)
            _notifications.NotifyStopped(session.Duration);
        await UpdateTooltipAsync();
    }

    private async Task OnReminderTickAsync()
    {
        var active = await _sessionService.GetActiveSessionAsync();
        if (active is not null)
            _notifications.NotifyReminder(active.Duration);
    }

    private async Task UpdateTooltipAsync()
    {
        var today = await _sessionService.GetSessionsForDayAsync(DateTime.Today);
        var total = SessionService.TotalDuration(today);
        var label = total.TotalHours >= 1
            ? $"Today: {(int)total.TotalHours}h {total.Minutes}m"
            : $"Today: {total.Minutes}m";
        _tray.UpdateTooltip($"SitTimer — {label}");
    }

    private double ReminderIntervalMs() =>
        Math.Max(_settings.ReminderMinutes, 1) * 60_000.0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _heartbeatTimer.Dispose();
        _tooltipTimer.Dispose();
        _reminderTimer.Dispose();
    }
}
