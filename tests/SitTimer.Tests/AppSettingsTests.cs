using SitTimer.Core.Services;

namespace SitTimer.Tests;

[TestFixture]
public class AppSettingsTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SitTimerTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    [Test]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultValues()
    {
        var settings = AppSettings.Load(_tempDir);

        Assert.That(settings.ReminderMinutes, Is.EqualTo(45));
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Test]
    public void SaveAndLoad_PersistsReminderMinutes()
    {
        var settings = AppSettings.Load(_tempDir);
        settings.ReminderMinutes = 30;
        settings.Save();

        var loaded = AppSettings.Load(_tempDir);

        Assert.That(loaded.ReminderMinutes, Is.EqualTo(30));
    }

    [Test]
    public void SaveAndLoad_PersistsReminderMinutes_Zero()
    {
        var settings = AppSettings.Load(_tempDir);
        settings.ReminderMinutes = 0;
        settings.Save();

        var loaded = AppSettings.Load(_tempDir);

        Assert.That(loaded.ReminderMinutes, Is.EqualTo(0));
    }

    [Test]
    public void Save_CreatesSettingsFile()
    {
        var settings = AppSettings.Load(_tempDir);
        settings.Save();

        Assert.That(File.Exists(Path.Combine(_tempDir, "settings.json")), Is.True);
    }

    // ── Corrupt file ──────────────────────────────────────────────────────────

    [Test]
    public void Load_WhenFileIsCorrupt_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "not valid json {{{{");

        var settings = AppSettings.Load(_tempDir);

        Assert.That(settings.ReminderMinutes, Is.EqualTo(45));
    }

    [Test]
    public void Load_WhenFileIsEmpty_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), string.Empty);

        var settings = AppSettings.Load(_tempDir);

        Assert.That(settings.ReminderMinutes, Is.EqualTo(45));
    }

    // ── Save guard ────────────────────────────────────────────────────────────

    [Test]
    public void Save_WithNoAppDataDir_DoesNotThrow()
    {
        var settings = new AppSettings();
        Assert.DoesNotThrow(() => settings.Save());
    }
}
