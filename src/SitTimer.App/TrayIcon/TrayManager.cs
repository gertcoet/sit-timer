using WpfApplication = System.Windows.Application;
using SitTimer.App.Views;

namespace SitTimer.App.TrayIcon;

public class TrayManager : IDisposable
{
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

        NotifyIcon = new NotifyIcon
        {
            Text = "SitTimer",
            Visible = true,
            Icon = LoadIcon()
        };

        NotifyIcon.MouseClick += OnMouseClick;
        NotifyIcon.ContextMenuStrip = BuildContextMenu();
    }

    private static Icon LoadIcon()
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Background: dark navy #1E3A5F
        g.Clear(Color.FromArgb(0x1E, 0x3A, 0x5F));

        // Clock face: white circle outline, inset 1 px on each side
        var faceRect = new Rectangle(1, 1, size - 3, size - 3);
        using var outlinePen = new Pen(Color.White, 1.0f);
        g.DrawEllipse(outlinePen, faceRect);

        // Centre of the clock face
        var cx = faceRect.X + faceRect.Width  / 2.0f;
        var cy = faceRect.Y + faceRect.Height / 2.0f;
        var radius = faceRect.Width / 2.0f;

        // Hour hand – pointing roughly to 12 (straight up, short)
        // Angle 0° = 3 o'clock in standard polar coords; -90° = 12 o'clock
        const double hourAngleRad = -Math.PI / 2.0;          // 12 o'clock
        const double minuteAngleRad = 0.0;                     // 3 o'clock

        var hourLen   = radius * 0.45f;
        var minuteLen = radius * 0.70f;

        using var handPen = new Pen(Color.White, 1.0f);

        // Hour hand
        g.DrawLine(handPen,
            cx, cy,
            cx + (float)(Math.Cos(hourAngleRad)   * hourLen),
            cy + (float)(Math.Sin(hourAngleRad)   * hourLen));

        // Minute hand
        g.DrawLine(handPen,
            cx, cy,
            cx + (float)(Math.Cos(minuteAngleRad) * minuteLen),
            cy + (float)(Math.Sin(minuteAngleRad) * minuteLen));

        // Convert bitmap to Icon
        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
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

    public NotifyIcon NotifyIcon { get; }

    public void UpdateTooltip(string text) => NotifyIcon.Text = text[..Math.Min(text.Length, 63)];

    public void Dispose()
    {
        NotifyIcon.Visible = false;
        NotifyIcon.Dispose();
    }
}
