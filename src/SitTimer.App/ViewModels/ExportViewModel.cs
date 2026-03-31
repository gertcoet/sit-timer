using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SitTimer.Core.Services;

namespace SitTimer.App.ViewModels;

public class ExportViewModel : INotifyPropertyChanged
{
    private readonly SessionService _sessionService;

    private bool _exportAll = true;
    private DateTime _fromDate = DateTime.Today.AddDays(-30);
    private DateTime _toDate = DateTime.Today;
    private bool _isCsv = true;

    public ExportViewModel(SessionService sessionService)
    {
        _sessionService = sessionService;
        ExportCommand = new RelayCommand(ExecuteExportAsync);
    }

    // ── Bindable properties ──────────────────────────────────────────────────

    public bool ExportAll
    {
        get => _exportAll;
        set
        {
            Set(ref _exportAll, value);
            OnPropertyChanged(nameof(IsRangeEnabled));
        }
    }

    public bool IsRangeEnabled => !_exportAll;

    public DateTime FromDate { get => _fromDate; set => Set(ref _fromDate, value); }
    public DateTime ToDate { get => _toDate; set => Set(ref _toDate, value); }
    public bool IsCsv { get => _isCsv; set => Set(ref _isCsv, value); }

    public ICommand ExportCommand { get; }

    // ── Delegates (set by code-behind) ───────────────────────────────────────

    public Func<ExportFormat, string?>? RequestSaveFilePath { get; set; }
    public Action? CloseDialog { get; set; }

    // ── Export logic ─────────────────────────────────────────────────────────

    private async Task ExecuteExportAsync()
    {
        var format = _isCsv ? ExportFormat.Csv : ExportFormat.Json;

        var path = RequestSaveFilePath?.Invoke(format);
        if (path is null) return;

        var sessions = _exportAll
            ? await _sessionService.GetAllSessionsAsync()
            : await _sessionService.GetSessionsForRangeAsync(_fromDate, _toDate);

        var content = ExportService.Export(sessions, format);
        await File.WriteAllTextAsync(path, content);

        CloseDialog?.Invoke();
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
