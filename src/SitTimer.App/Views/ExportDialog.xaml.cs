using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SitTimer.App.ViewModels;
using SitTimer.Core.Services;
using WpfApplication = System.Windows.Application;

namespace SitTimer.App.Views;

public partial class ExportDialog : Window
{
    public ExportDialog()
    {
        InitializeComponent();

        var app = (App)WpfApplication.Current;
        var sessionService = app.Services!.GetRequiredService<SessionService>();

        var vm = new ExportViewModel(sessionService)
        {
            RequestSaveFilePath = format =>
            {
                var ext = format == ExportFormat.Csv ? "csv" : "json";
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"sit-timer-export.{ext}",
                    Filter = format == ExportFormat.Csv
                        ? "CSV files (*.csv)|*.csv"
                        : "JSON files (*.json)|*.json",
                    DefaultExt = ext
                };
                return dialog.ShowDialog(this) == true ? dialog.FileName : null;
            },
            CloseDialog = () =>
            {
                DialogResult = true;
                Close();
            }
        };

        DataContext = vm;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
