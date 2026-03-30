using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SitTimer.Core.Services;

namespace SitTimer.App.ViewModels;

public enum DashboardTab { Day, Week, Month, Year }

public class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SessionService _sessionService;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _liveTimer;

    private DashboardTab _activeTab = DashboardTab.Day;
    private DateTime _selectedDate = DateTime.Today;
    private string _headerText = string.Empty;
    private string _totalText = string.Empty;
    private ObservableCollection<SessionRowViewModel> _sessions = [];
    private ISeries[] _chartSeries = [];
    private Axis[] _xAxes = [];
    private bool _showTable = true;
    private int _reminderMinutes;

    public DashboardViewModel(SessionService sessionService, AppSettings appSettings, Action<int> onReminderChanged)
    {
        _sessionService = sessionService;
        _appSettings = appSettings;
        var onReminderChanged1 = onReminderChanged;
        _reminderMinutes = appSettings.ReminderMinutes;

        PreviousCommand = new RelayCommand(async () => { Navigate(-1); await LoadAsync(); });
        NextCommand = new RelayCommand(async () => { Navigate(1); await LoadAsync(); });
        SelectDayCommand = new RelayCommand(async () => { _activeTab = DashboardTab.Day; await LoadAsync(); });
        SelectWeekCommand = new RelayCommand(async () => { _activeTab = DashboardTab.Week; await LoadAsync(); });
        SelectMonthCommand = new RelayCommand(async () => { _activeTab = DashboardTab.Month; await LoadAsync(); });
        SelectYearCommand = new RelayCommand(async () => { _activeTab = DashboardTab.Year; await LoadAsync(); });
        SaveReminderCommand = new RelayCommand(() =>
        {
            onReminderChanged1(_reminderMinutes);
            _appSettings.Save();
            return Task.CompletedTask;
        });

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _liveTimer.Tick += async (_, _) => await LoadAsync();
        _liveTimer.Start();
    }

    // ── Bindable properties ──────────────────────────────────────────────────

    public string HeaderText { get => _headerText; private set => Set(ref _headerText, value); }
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    public ObservableCollection<SessionRowViewModel> Sessions { get => _sessions; private set => Set(ref _sessions, value); }
    public ISeries[] ChartSeries { get => _chartSeries; private set => Set(ref _chartSeries, value); }
    public Axis[] XAxes { get => _xAxes; private set => Set(ref _xAxes, value); }
    public bool ShowTable { get => _showTable; private set => Set(ref _showTable, value); }

    public int ReminderMinutes
    {
        get => _reminderMinutes;
        set
        {
            if (value < 1) value = 1;
            Set(ref _reminderMinutes, value);
            _appSettings.ReminderMinutes = value;
        }
    }

    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SelectDayCommand { get; }
    public ICommand SelectWeekCommand { get; }
    public ICommand SelectMonthCommand { get; }
    public ICommand SelectYearCommand { get; }
    public ICommand SaveReminderCommand { get; }

    // ── Loading ──────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        switch (_activeTab)
        {
            case DashboardTab.Day: await LoadDayAsync(); break;
            case DashboardTab.Week: await LoadWeekAsync(); break;
            case DashboardTab.Month: await LoadMonthAsync(); break;
            case DashboardTab.Year: await LoadYearAsync(); break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task LoadDayAsync()
    {
        ShowTable = true;
        var sessions = await _sessionService.GetSessionsForDayAsync(_selectedDate);
        var total = SessionService.TotalDuration(sessions);

        HeaderText = _selectedDate.ToString("dddd d MMMM");
        TotalText = FormatDuration(total);

        Sessions = new ObservableCollection<SessionRowViewModel>(
            sessions.Select(s => new SessionRowViewModel(s, DeleteSessionAsync)));
    }

    private async Task LoadWeekAsync()
    {
        ShowTable = false;
        var monday = _selectedDate.AddDays(-(int)_selectedDate.DayOfWeek + (int)DayOfWeek.Monday);
        if (_selectedDate.DayOfWeek == DayOfWeek.Sunday) monday = monday.AddDays(-7);

        var sessions = await _sessionService.GetSessionsForWeekAsync(monday);
        var byDay = SessionService.GroupByDay(sessions);

        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToArray();
        var values = days.Select(d => byDay.TryGetValue(d, out var h) ? h : 0.0).ToArray();

        HeaderText = $"{monday:d MMM} – {monday.AddDays(6):d MMM}";
        TotalText = FormatDuration(SessionService.TotalDuration(sessions));

        SetBarChart(days.Select(d => d.ToString("ddd")).ToArray(), values);
    }

    private async Task LoadMonthAsync()
    {
        ShowTable = false;
        var sessions = await _sessionService.GetSessionsForMonthAsync(_selectedDate.Year, _selectedDate.Month);
        var byDay = SessionService.GroupByDay(sessions);

        var daysInMonth = DateTime.DaysInMonth(_selectedDate.Year, _selectedDate.Month);
        var days = Enumerable.Range(1, daysInMonth)
            .Select(d => new DateTime(_selectedDate.Year, _selectedDate.Month, d))
            .ToArray();
        var values = days.Select(d => byDay.TryGetValue(d, out var h) ? h : 0.0).ToArray();

        HeaderText = _selectedDate.ToString("MMMM yyyy");
        TotalText = FormatDuration(SessionService.TotalDuration(sessions));

        SetBarChart(days.Select(d => d.Day.ToString()).ToArray(), values);
    }

    private async Task LoadYearAsync()
    {
        ShowTable = false;
        var sessions = await _sessionService.GetSessionsForYearAsync(_selectedDate.Year);
        var byMonth = SessionService.GroupByMonth(sessions);

        var months = Enumerable.Range(1, 12)
            .Select(m => new DateTime(_selectedDate.Year, m, 1))
            .ToArray();
        var values = months.Select(m => byMonth.TryGetValue(m, out var h) ? h : 0.0).ToArray();

        HeaderText = _selectedDate.Year.ToString();
        TotalText = FormatDuration(SessionService.TotalDuration(sessions));

        SetBarChart(months.Select(m => m.ToString("MMM")).ToArray(), values);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Navigate(int delta)
    {
        _selectedDate = _activeTab switch
        {
            DashboardTab.Day => _selectedDate.AddDays(delta),
            DashboardTab.Week => _selectedDate.AddDays(delta * 7),
            DashboardTab.Month => _selectedDate.AddMonths(delta),
            DashboardTab.Year => _selectedDate.AddYears(delta),
            _ => _selectedDate
        };
    }

    private void SetBarChart(string[] labels, double[] values)
    {
        ChartSeries =
        [
            new ColumnSeries<double>
            {
                Values = values,
                Name = "Hours",
                Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(
                    new SkiaSharp.SKColor(79, 140, 255))
            }
        ];
        XAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsRotation = labels.Length > 10 ? 45 : 0
            }
        ];
    }

    private async Task DeleteSessionAsync(int id)
    {
        await _sessionService.DeleteSessionAsync(id);
        await LoadAsync();
    }

    private static string FormatDuration(TimeSpan ts)
    {
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m";
    }

    public void Dispose() => _liveTimer.Stop();

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
