using System.Text.Json;

namespace SitTimer.Core.Services;

public class AppSettings
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };
    private string _appDataDir = string.Empty;

    public int ReminderMinutes { get; set; } = 45;
    public int MinBreakMinutes { get; set; } = 2;

    public static AppSettings Load(string appDataDir)
    {
        var path = SettingsPath(appDataDir);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                {
                    loaded._appDataDir = appDataDir;
                    return loaded;
                }
            }
            catch { /* corrupt file — use defaults */ }
        }

        return new AppSettings { _appDataDir = appDataDir };
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(_appDataDir)) return;
        var json = JsonSerializer.Serialize(this, _jsonOpts);
        File.WriteAllText(SettingsPath(_appDataDir), json);
    }

    private static string SettingsPath(string dir) =>
        Path.Combine(dir, "settings.json");
}
