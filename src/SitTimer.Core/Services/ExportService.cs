using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SitTimer.Core.Models;

namespace SitTimer.Core.Services;

public enum ExportFormat { Csv, Json }

public record ExportRow(string Date, string Start, string End, string Duration, string Break);

public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static IReadOnlyList<ExportRow> ToExportRows(IReadOnlyList<Session> sessions)
    {
        return sessions.Select(s =>
        {
            var startLocal = s.StartTime.ToLocalTime();
            var endLocal = s.EndTime?.ToLocalTime();
            return new ExportRow(
                startLocal.ToString("yyyy-MM-dd"),
                startLocal.ToString("HH:mm:ss"),
                endLocal?.ToString("HH:mm:ss") ?? "",
                FormatDuration(s.Duration),
                s.BreakTime.HasValue ? FormatDuration(s.BreakTime.Value) : "");
        }).ToList();
    }

    public static string ToCsv(IReadOnlyList<Session> sessions)
    {
        var rows = ToExportRows(sessions);
        var sb = new StringBuilder();
        sb.AppendLine("Date,Start,End,Duration,Break");
        foreach (var r in rows)
            sb.AppendLine($"{r.Date},{r.Start},{r.End},{r.Duration},{r.Break}");
        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<Session> sessions)
    {
        var rows = ToExportRows(sessions);
        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    public static string Export(IReadOnlyList<Session> sessions, ExportFormat format) =>
        format switch
        {
            ExportFormat.Csv => ToCsv(sessions),
            ExportFormat.Json => ToJson(sessions),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m";
}
