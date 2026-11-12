using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Walls;

/// <summary>
/// dispatch/WALLS.md as text: one bullet a wall, what only Steve can clear. A bullet a process writes ends with a
/// marker comment naming its key and the last time the wall was met, so the process can find its own bullet again,
/// refresh the time without rewording what Steve reads, and take it away when the wall is gone. Bullets other lanes
/// wrote by hand carry no marker and are never touched. With no bullet left the file says "- none", as it did.
/// </summary>
public static partial class WallList
{
    /// <summary>The file on the share.</summary>
    public const string Path = "dispatch/WALLS.md";

    /// <summary>The last time the keyed wall was met, or null when the file holds no bullet for it.</summary>
    public static DateTimeOffset? Last(string file, string key) =>
        Lines(file).Select(l => Marker().Match(l)).FirstOrDefault(m => m.Success && m.Groups[1].Value == key) is { } m
            ? DateTimeOffset.ParseExact(m.Groups[2].Value, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : null;

    /// <summary>
    /// The file with the keyed bullet in it: added after the last bullet with "- none" taken away, or, when it is
    /// there already, its words kept (they say since when) and only its time moved to <paramref name="last"/>.
    /// </summary>
    public static string With(string file, string key, string bullet, DateTimeOffset last)
    {
        var lines = Lines(file);
        string marker = $"<!-- wall:{key} last={last.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)} -->";
        int at = lines.FindIndex(l => Holds(l, key));
        if (at >= 0)
        {
            lines[at] = Marker().Replace(lines[at], marker);
            return string.Join('\n', lines);
        }
        lines.RemoveAll(IsNone);
        int after = lines.FindLastIndex(l => l.StartsWith("- ", StringComparison.Ordinal));
        while (lines.Count > 0 && lines[^1].Trim().Length == 0 && after < lines.Count - 1)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        lines.Insert(after >= 0 ? after + 1 : lines.Count, $"- {bullet} {marker}");
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>The file without the keyed bullet; "- none" comes back when it was the last one.</summary>
    public static string Without(string file, string key)
    {
        var lines = Lines(file);
        int at = lines.FindIndex(l => Holds(l, key));
        if (at < 0)
        {
            return file;
        }
        lines.RemoveAt(at);
        if (!lines.Any(l => l.StartsWith("- ", StringComparison.Ordinal)))
        {
            lines.Insert(at, "- none");
        }
        return string.Join('\n', lines);
    }

    private static List<string> Lines(string file) => [.. file.Split('\n')];

    private static bool Holds(string line, string key) => Marker().Match(line) is { Success: true } m && m.Groups[1].Value == key;

    private static bool IsNone(string line) => line.StartsWith("- none", StringComparison.Ordinal);

    [GeneratedRegex(@"<!-- wall:([a-z0-9-]+) last=(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z) -->")]
    private static partial Regex Marker();
}
