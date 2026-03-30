using System.Windows.Input;
using SitTimer.Core.Models;

namespace SitTimer.App.ViewModels;

public class SessionRowViewModel
{
    private readonly Session _session;
    private readonly Func<int, Task> _deleteCallback;

    public SessionRowViewModel(Session session, Func<int, Task> deleteCallback)
    {
        _session = session;
        _deleteCallback = deleteCallback;
        DeleteCommand = new RelayCommand(async () => await _deleteCallback(_session.Id));
    }

    public string Start => _session.StartTime.ToLocalTime().ToString("HH:mm");

    public string End => _session.IsActive
        ? "—"
        : _session.EndTime!.Value.ToLocalTime().ToString("HH:mm");

    public string Duration
    {
        get
        {
            var d = _session.Duration;
            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}h {d.Minutes}m"
                : $"{d.Minutes}m";
        }
    }

    public bool IsActive => _session.IsActive;

    public ICommand DeleteCommand { get; }
}

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;

    public RelayCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter) => await _execute();
}
