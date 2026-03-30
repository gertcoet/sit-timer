using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SitTimer.App.Notifications;
using SitTimer.App.TrayIcon;
using SitTimer.Core.Data;
using SitTimer.Core.Services;
using System.IO;
using System.Threading.Tasks;
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
    public SessionMonitor Monitor => _monitor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _ = OnStartupAsync(e);
    }

    private async Task OnStartupAsync(StartupEventArgs e)
    {
        try
        {
            _services = BuildServices();

            var db = _services.GetRequiredService<SitTimerDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureSchemaUpToDateAsync();

            var sessionService = _services.GetRequiredService<SessionService>();
            var notifications = _services.GetRequiredService<NotificationService>();
            var appSettings = _services.GetRequiredService<AppSettings>();

            await sessionService.BackfillBreakTimesAsync();

            _tray = new TrayManager(
                isSessionActive: () => sessionService.GetActiveSessionAsync().GetAwaiter().GetResult() is not null,
                onManualStart: () => _monitor.ManualStartAsync().GetAwaiter().GetResult(),
                onManualStop: () => _monitor.ManualStopAsync().GetAwaiter().GetResult());

            notifications.Attach(_tray.NotifyIcon);
            _monitor = new SessionMonitor(sessionService, notifications, _tray, appSettings);
            await _monitor.InitialiseAsync();

            StartupHelper.EnsureAutoStart();
        }
        catch
        {
            Shutdown(-1);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Must block here — async void would let the process exit before the session is saved.
        var sessionService = _services.GetRequiredService<SessionService>();
        sessionService.StopActiveSessionAsync().GetAwaiter().GetResult();

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

        var settings = AppSettings.Load(appData);

        var services = new ServiceCollection();
        services.AddDbContext<SitTimerDbContext>(opts =>
            opts.UseSqlite($"Data Source={dbPath}"));
        services.AddTransient<SessionService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton(settings);
        return services.BuildServiceProvider();
    }
}
