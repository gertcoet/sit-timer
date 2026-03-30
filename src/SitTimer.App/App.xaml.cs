using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SitTimer.App.Notifications;
using SitTimer.App.TrayIcon;
using SitTimer.Core.Data;
using SitTimer.Core.Services;
using System.IO;
using Application = System.Windows.Application;
using ExitEventArgs = System.Windows.ExitEventArgs;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace SitTimer.App;

public partial class App : Application
{
    private ServiceProvider _services = null!;
    private TrayManager _tray = null!;
    private SessionMonitor _monitor = null!;

    public IServiceProvider Services => _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = BuildServices();

        var db = _services.GetRequiredService<SitTimerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var sessionService = _services.GetRequiredService<SessionService>();
        var notifications = _services.GetRequiredService<NotificationService>();

        _tray = new TrayManager(
            isSessionActive: () => sessionService.GetActiveSessionAsync().GetAwaiter().GetResult() is not null,
            onManualStart: () => _monitor.ManualStartAsync().GetAwaiter().GetResult(),
            onManualStop: () => _monitor.ManualStopAsync().GetAwaiter().GetResult());

        notifications.Attach(_tray.NotifyIcon);
        _monitor = new SessionMonitor(sessionService, notifications, _tray);
        await _monitor.InitialiseAsync();

        StartupHelper.EnsureAutoStart();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var sessionService = _services.GetRequiredService<SessionService>();
        await sessionService.StopActiveSessionAsync();

        _monitor.Dispose();
        _tray.Dispose();
        _services.Dispose();

        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SitTimer");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "sittimer.db");

        var services = new ServiceCollection();
        services.AddDbContext<SitTimerDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));
        services.AddTransient<SessionService>();
        services.AddSingleton<NotificationService>();
        return services.BuildServiceProvider();
    }
}
