using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SitTimer.Core.Models;

namespace SitTimer.App.ViewModels;

public abstract class SessionDisplayItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public abstract string Start { get; }
    public abstract string End { get; }
    public abstract string Duration { get; }
    public abstract string Break { get; }
    public abstract bool IsActive { get; }
    public abstract bool CanSelect { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m";
}

public class StandaloneSessionItem : SessionDisplayItem
{
    private readonly Session _session;

    public StandaloneSessionItem(Session session, Func<int, Task> deleteCallback)
    {
        _session = session;
        DeleteCommand = new RelayCommand(() => deleteCallback(_session.Id));
    }

    public int SessionId => _session.Id;
    public override string Start => _session.StartTime.ToLocalTime().ToString("HH:mm");
    public override string End => _session.IsActive ? "\u2014" : _session.EndTime!.Value.ToLocalTime().ToString("HH:mm");
    public override string Duration => FormatDuration(_session.Duration);

    public override string Break
    {
        get
        {
            var b = _session.BreakTime;
            if (b is null) return "\u2014";
            return b.Value.TotalHours >= 1
                ? $"{(int)b.Value.TotalHours}h {b.Value.Minutes}m"
                : $"{b.Value.Minutes}m";
        }
    }

    public override bool IsActive => _session.IsActive;
    public override bool CanSelect => !_session.IsActive;
    public ICommand DeleteCommand { get; }
}

public class MergeGroupHeaderItem : SessionDisplayItem
{
    private bool _isExpanded;
    private readonly IReadOnlyList<Session> _sessions;

    public MergeGroupHeaderItem(
        string mergeGroupId,
        IReadOnlyList<Session> groupSessions,
        Func<string, Task> unmergeCallback)
    {
        MergeGroupId = mergeGroupId;
        _sessions = groupSessions;
        Children = groupSessions
            .OrderBy(s => s.StartTime)
            .Select(s => new MergeGroupChildItem(s))
            .ToList();
        UnmergeCommand = new RelayCommand(() => unmergeCallback(mergeGroupId));
        ToggleExpandCommand = new RelayCommand(() =>
        {
            IsExpanded = !IsExpanded;
            return Task.CompletedTask;
        });
    }

    public string MergeGroupId { get; }
    public List<MergeGroupChildItem> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandArrow)); }
    }

    public string ExpandArrow => _isExpanded ? "\u25BC" : "\u25B6";

    public ICommand ToggleExpandCommand { get; }
    public ICommand UnmergeCommand { get; }

    public override string Start => _sessions.Min(s => s.StartTime).ToLocalTime().ToString("HH:mm");

    public override string End
    {
        get
        {
            if (_sessions.Any(s => s.IsActive)) return "\u2014";
            return _sessions.Max(s => s.EndTime!.Value).ToLocalTime().ToString("HH:mm");
        }
    }

    public override string Duration
    {
        get
        {
            var total = _sessions.Aggregate(TimeSpan.Zero, (acc, s) => acc + s.Duration);
            return FormatDuration(total);
        }
    }

    public override string Break
    {
        get
        {
            var first = _sessions.OrderBy(s => s.StartTime).First();
            if (first.BreakTime is null) return "\u2014";
            return FormatDuration(first.BreakTime.Value);
        }
    }

    public override bool IsActive => _sessions.Any(s => s.IsActive);
    public override bool CanSelect => false;
}

public class MergeGroupChildItem : SessionDisplayItem
{
    private readonly Session _session;

    public MergeGroupChildItem(Session session) => _session = session;

    public override string Start => _session.StartTime.ToLocalTime().ToString("HH:mm");
    public override string End => _session.IsActive ? "\u2014" : _session.EndTime!.Value.ToLocalTime().ToString("HH:mm");
    public override string Duration => FormatDuration(_session.Duration);

    public override string Break
    {
        get
        {
            var b = _session.BreakTime;
            if (b is null) return "\u2014";
            return b.Value.TotalHours >= 1
                ? $"{(int)b.Value.TotalHours}h {b.Value.Minutes}m"
                : $"{b.Value.Minutes}m";
        }
    }

    public override bool IsActive => _session.IsActive;
    public override bool CanSelect => false;
}
