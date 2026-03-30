using System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using SitTimer.App.Views;

namespace SitTimer.App.TrayIcon;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Func<bool> _isSessionActive;
    private readonly Action _onManualStart;
    private readonly Action _onManualStop;
    private DashboardWindow? _dashboard;

    private ToolStripMenuItem _startItem = null!;
    private ToolStripMenuItem _stopItem = null!;

    public TrayManager(
        Func<bool> isSessionActive,
        Action onManualStart,
        Action onManualStop)
    {
        _isSessionActive = isSessionActive;
        _onManualStart = onManualStart;
        _onManualStop = onManualStop;

        _notifyIcon = new NotifyIcon
        {
            Text = "SitTimer",
            Visible = true,
            Icon = LoadIcon()
        };

        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        // Use a built-in system icon as placeholder until a custom icon is provided
        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        _startItem = new ToolStripMenuItem("Manual Start", null, (_, _) => _onManualStart());
        _stopItem = new ToolStripMenuItem("Manual Stop", null, (_, _) => _onManualStop());
        var separator = new ToolStripSeparator();
        var quit = new ToolStripMenuItem("Quit", null, (_, _) => WpfApplication.Current.Shutdown());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_startItem, _stopItem, separator, quit]);
        menu.Opening += (_, _) => RefreshMenuState();
        return menu;
    }

    private void RefreshMenuState()
    {
        var active = _isSessionActive();
        _startItem.Enabled = !active;
        _stopItem.Enabled = active;
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            OpenDashboard();
    }

    private void OpenDashboard()
    {
        if (_dashboard is null || !_dashboard.IsLoaded)
        {
            _dashboard = new DashboardWindow();
            _dashboard.Show();
        }
        else
        {
            _dashboard.Activate();
        }
    }

    public NotifyIcon NotifyIcon => _notifyIcon;

    public void UpdateTooltip(string text) => _notifyIcon.Text = text[..Math.Min(text.Length, 63)];

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
