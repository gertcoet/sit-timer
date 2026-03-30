using System.Diagnostics;
using System.Windows.Input;
using SitTimer.Core.Models;

namespace SitTimer.App.ViewModels;

public class SessionRowViewModel
{
    private readonly Session _session;

    public SessionRowViewModel(Session session, Func<int, Task> deleteCallback)
    {
        _session = session;
        var deleteCallback1 = deleteCallback;
        DeleteCommand = new RelayCommand(() => deleteCallback1(_session.Id));
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

    public string Break
    {
        get
        {
            var b = _session.BreakTime;
            if (b is null) return "—";
            return b.Value.TotalHours >= 1
                ? $"{(int)b.Value.TotalHours}h {b.Value.Minutes}m"
                : $"{b.Value.Minutes}m";
        }
    }

    public bool IsActive => _session.IsActive;

    public ICommand DeleteCommand { get; }
}

public class RelayCommand(Func<Task> execute) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    private async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            await execute().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
