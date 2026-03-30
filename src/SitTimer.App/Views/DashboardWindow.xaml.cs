using Microsoft.Extensions.DependencyInjection;
using SitTimer.App.ViewModels;
using SitTimer.Core.Services;
using WpfApplication = System.Windows.Application;
using System.Windows;

namespace SitTimer.App.Views;

public partial class DashboardWindow : Window
{
    private DashboardViewModel? _viewModel;

    public DashboardWindow()
    {
        InitializeComponent();
    }

    protected override async void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (_viewModel is null)
        {
            var app = (App)WpfApplication.Current;
            var sessionService = app.Services.GetRequiredService<SessionService>();
            var appSettings   = app.Services.GetRequiredService<AppSettings>();

            _viewModel = new DashboardViewModel(sessionService, appSettings, app.Monitor.UpdateReminderInterval);
            DataContext = _viewModel;
            await _viewModel.LoadAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
