using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// What the gate caught, read from data/gate-reds.json: every gate run that stopped a version from shipping, which
/// promote keeps since a red gate never reaches main (scripts/gate_reds.mjs). Built with AI's third column reads it
/// as {{live:gate-caught}} (LiveCounts).
/// </summary>
public static class GateReds
{
    /// <summary>"66 times between 14 October and 17 November; the latest, 1.0.5.80, at its cosmos job".</summary>
    public static string Caught(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "gate-reds.json")));
        JsonElement[] rows = [.. document.RootElement.GetProperty("rows").EnumerateArray()];
        if (rows.Length == 0) return "nothing yet: no gate run has stopped a version";
        JsonElement newest = rows[0];
        string[] failed = [.. newest.GetProperty("failed").EnumerateArray().Select(job => job.GetString() ?? "")];
        string where = failed.Length == 0 ? "at its verdict" : $"at its {string.Join(" and ", failed)} job{(failed.Length == 1 ? "" : "s")}";
        return string.Create(CultureInfo.InvariantCulture,
            $"{rows.Length} times between {Day(rows[^1])} and {Day(newest)}; the latest, {newest.GetProperty("version").GetString()}, {where}");
    }

    /// <summary>A row's push day in Central time, as the changelog dates versions and the pages write it: "17 November".</summary>
    private static string Day(JsonElement row)
    {
        DateTimeOffset at = DateTimeOffset.Parse(row.GetProperty("at").GetString()!, CultureInfo.InvariantCulture);
        return TimeZoneInfo.ConvertTime(at, Central.Value).ToString("d MMMM", CultureInfo.InvariantCulture);
    }

    /// <summary>Central time, or UTC on a machine whose zone list does not carry it.</summary>
    private static readonly Lazy<TimeZoneInfo> Central = new(() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("America/Chicago", out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc);
}
