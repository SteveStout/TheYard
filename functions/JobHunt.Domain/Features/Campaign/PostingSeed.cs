using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The first harvest's file (staging/dotnetbook/handoff/postings.jsonl, 593 .NET postings from 243 boards) read as
/// posting rows, so the postings' history starts with them instead of nothing. A line's band is the board's own
/// compensation summary ("$145K – $175K"); a summary in another currency or without two figures is no band. The
/// board's id is the address's last segment. A line that does not read is skipped and counted.
/// </summary>
public static partial class PostingSeed
{
    /// <summary>The file on the share.</summary>
    public const string File = "staging/dotnetbook/handoff/postings.jsonl";

    /// <summary>The file's postings and how many lines did not read.</summary>
    public static (IReadOnlyList<Posting> Postings, int Unread) Parse(string text)
    {
        var postings = new List<Posting>();
        int unread = 0;
        foreach (string line in (text ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                string source = Text(root, "source"), slug = Text(root, "slug"), url = Text(root, "url"), title = Text(root, "title");
                string id = url.TrimEnd('/').Split('/')[^1];
                if (source is not ("greenhouse" or "lever" or "ashby") || slug.Length == 0 || id.Length == 0 || title.Length == 0)
                {
                    unread++;
                    continue;
                }
                var (min, max) = Band(Text(root, "comp"));
                string location = Text(root, "location");
                bool? remote = root.TryGetProperty("remote", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False ? r.GetBoolean() : null;
                DateOnly? posted = DateTimeOffset.TryParse(Text(root, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? DateOnly.FromDateTime(at.UtcDateTime) : null;
                postings.Add(new Posting(source, slug, id, title, location, url, posted)
                {
                    RemoteStance = PostingRules.Stance(null, location, remote),
                    CompMin = min,
                    CompMax = max,
                    CompCurrency = min is null ? null : "USD",
                    CompInterval = min is null ? null : "year",
                    Text = Text(root, "line"),
                });
            }
            catch (JsonException)
            {
                unread++;
            }
        }
        return ([.. postings.DistinctBy(p => (p.Source, p.BoardToken, p.PostingId))], unread);
    }

    /// <summary>The two dollar figures of a summary ("$145K – $175K", "$211.4K - $290.6K", "$120,000 - $150,000") in whole dollars, or nulls.</summary>
    public static (int? Min, int? Max) Band(string? summary)
    {
        var figures = Dollars().Matches(summary ?? string.Empty)
            .Select(m => decimal.Parse(m.Groups[1].Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture) * (m.Groups[2].Success ? 1000 : 1))
            .Take(2).ToList();
        return figures.Count == 2 ? ((int)figures.Min(), (int)figures.Max()) : (null, null);
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";

    [GeneratedRegex(@"\$\s?(\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)\s?([Kk])?")]
    private static partial Regex Dollars();
}
