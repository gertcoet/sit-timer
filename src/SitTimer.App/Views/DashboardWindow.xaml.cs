using Microsoft.Extensions.DependencyInjection;
using SitTimer.App.ViewModels;
using SitTimer.Core.Services;
using WpfApplication = System.Windows.Application;
using System.Windows;
using LiveChartsCore;

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
            if (app.Services is null || app.Monitor is null) return;
            var sessionService = app.Services.GetRequiredService<SessionService>();
            var appSettings   = app.Services.GetRequiredService<AppSettings>();

            _viewModel = new DashboardViewModel(sessionService, appSettings, app.Monitor.UpdateReminderInterval);
            _viewModel.RequestOpenExportDialog = () =>
            {
                var dialog = new ExportDialog { Owner = this };
                dialog.ShowDialog();
            };
            DataContext = _viewModel;

            BarChart.DataPointerDown += (sender, points) =>
            {
                var point = System.Linq.Enumerable.FirstOrDefault(points);
                if (point is null) return;
                _ = _viewModel.DrillToDayAsync((int)Math.Round(point.Coordinate.SecondaryValue));
            };

            await _viewModel.LoadAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
