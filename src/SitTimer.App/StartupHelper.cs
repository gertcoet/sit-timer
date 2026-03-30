using Microsoft.Win32;

namespace SitTimer.App;

public static class StartupHelper
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SitTimer";

    public static void EnsureAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        var exePath = Environment.ProcessPath;
        if (exePath is null) return;

        var existing = key.GetValue(AppName) as string;
        if (!string.Equals(existing, exePath, StringComparison.OrdinalIgnoreCase))
            key.SetValue(AppName, exePath);
    }
}
