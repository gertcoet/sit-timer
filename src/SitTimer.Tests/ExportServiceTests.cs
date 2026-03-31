using System.Text.Json;
using SitTimer.Core.Models;
using SitTimer.Core.Services;

namespace SitTimer.Tests;

[TestFixture]
public class ExportServiceTests
{
    private static Session MakeSession(DateTime startUtc, DateTime? endUtc, TimeSpan? breakTime = null) =>
        new() { Id = 1, StartTime = startUtc, EndTime = endUtc, LastHeartbeat = startUtc, BreakTime = breakTime };

    // ── CSV ──────────────────────────────────────────────────────────────────

    [Test]
    public void ToCsv_EmptySessions_ReturnsHeaderOnly()
    {
        var csv = ExportService.ToCsv([]);
        var lines = csv.TrimEnd().Split('\n');
        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0].Trim(), Is.EqualTo("Date,Start,End,Duration,Break,MergeGroupId"));
    }

    [Test]
    public void ToCsv_SingleSession_FormatsCorrectly()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1).AddMinutes(30);
        var sessions = new List<Session> { MakeSession(start, end, TimeSpan.FromMinutes(5)) };

        var csv = ExportService.ToCsv(sessions);
        var lines = csv.TrimEnd().Split('\n');

        Assert.That(lines, Has.Length.EqualTo(2));
        var cols = lines[1].Trim().Split(',');
        Assert.That(cols, Has.Length.EqualTo(6));
        Assert.That(cols[3], Is.EqualTo("1h 30m")); // Duration
        Assert.That(cols[4], Is.EqualTo("5m"));      // Break
        Assert.That(cols[5], Is.EqualTo(""));         // MergeGroupId (empty for non-merged)
    }

    [Test]
    public void ToCsv_ActiveSession_EndTimeIsEmpty()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var sessions = new List<Session> { MakeSession(start, null) };

        var csv = ExportService.ToCsv(sessions);
        var line = csv.TrimEnd().Split('\n')[1].Trim();
        var cols = line.Split(',');

        Assert.That(cols[2], Is.EqualTo("")); // End is empty
    }

    [Test]
    public void ToCsv_NullBreakTime_BreakColumnIsEmpty()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1);
        var sessions = new List<Session> { MakeSession(start, end) };

        var csv = ExportService.ToCsv(sessions);
        var cols = csv.TrimEnd().Split('\n')[1].Trim().Split(',');

        Assert.That(cols[4], Is.EqualTo("")); // Break is empty
    }

    [Test]
    public void ToCsv_MultipleSessions_LineCountMatches()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var sessions = new List<Session>
        {
            MakeSession(start, start.AddHours(1)),
            MakeSession(start.AddHours(2), start.AddHours(3)),
            MakeSession(start.AddHours(4), start.AddHours(5))
        };

        var csv = ExportService.ToCsv(sessions);
        var lines = csv.TrimEnd().Split('\n');

        Assert.That(lines, Has.Length.EqualTo(4)); // header + 3 rows
    }

    // ── JSON ─────────────────────────────────────────────────────────────────

    [Test]
    public void ToJson_EmptySessions_ReturnsEmptyArray()
    {
        var json = ExportService.ToJson([]);
        Assert.That(json.Trim(), Is.EqualTo("[]"));
    }

    [Test]
    public void ToJson_SingleSession_DeserializesCorrectly()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(1).AddMinutes(30);
        var sessions = new List<Session> { MakeSession(start, end, TimeSpan.FromMinutes(5)) };

        var json = ExportService.ToJson(sessions);
        var rows = JsonSerializer.Deserialize<List<ExportRow>>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows![0].Duration, Is.EqualTo("1h 30m"));
        Assert.That(rows[0].Break, Is.EqualTo("5m"));
    }

    [Test]
    public void ToJson_UsesCamelCasePropertyNames()
    {
        var start = new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        var sessions = new List<Session> { MakeSession(start, start.AddHours(1)) };

        var json = ExportService.ToJson(sessions);

        Assert.That(json, Does.Contain("\"date\""));
        Assert.That(json, Does.Contain("\"start\""));
        Assert.That(json, Does.Contain("\"duration\""));
        Assert.That(json, Does.Not.Contain("\"Date\""));
    }

    // ── Export dispatch ──────────────────────────────────────────────────────

    [Test]
    public void Export_Csv_ReturnsCsvFormat()
    {
        var result = ExportService.Export([], ExportFormat.Csv);
        Assert.That(result, Does.StartWith("Date,Start,End,Duration,Break,MergeGroupId"));
    }

    [Test]
    public void Export_Json_ReturnsJsonFormat()
    {
        var result = ExportService.Export([], ExportFormat.Json);
        Assert.That(result.Trim(), Is.EqualTo("[]"));
    }
}
