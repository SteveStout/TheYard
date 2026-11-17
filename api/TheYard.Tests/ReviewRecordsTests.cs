using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The review route's stops each name the decision record behind them (ADR: Ten minutes, seven stops, the
/// addendum on the records): read from the one list the page draws, seven slugs, seven distinct, each one the
/// API's catalogue serves as a decision record, and none the address its own stop opens.
/// </summary>
public sealed partial class ReviewRecordsTests
{
    [GeneratedRegex(@"address: '([^']*)',[\s\S]*?record: '([^']*)',")]
    private static partial Regex StopRecord();

    [GeneratedRegex(@"address: '/\?doc=([^']*)',[\s\S]*?doThis:\s*'([^']*)',")]
    private static partial Regex StopDoThis();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex Quoted();

    [Fact]
    public void Every_stop_names_a_record_the_catalogue_serves_seven_distinct_none_its_own_address()
    {
        string stops = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "reviewStops.ts"));
        var pairs = StopRecord().Matches(stops).Select(m => (Address: m.Groups[1].Value, Record: m.Groups[2].Value)).ToList();

        Assert.Equal(7, pairs.Count);
        Assert.Equal(7, pairs.Select(p => p.Record).Distinct(StringComparer.Ordinal).Count());
        Assert.All(pairs, p =>
        {
            Assert.StartsWith("adr-", p.Record, StringComparison.Ordinal);
            Assert.True(DocumentationCatalog.Files.ContainsKey(p.Record), $"{p.Record} is not a document the catalogue serves");
            Assert.NotEqual($"/?doc={p.Record}", p.Address);
        });
    }

    [Fact]
    public void Every_heading_a_stop_tells_the_reader_to_scroll_to_is_a_heading_on_the_page_it_opens()
    {
        // Stop 1 sends the reader to "The slip, and the turn"; a rewrite of the page that dropped or renamed that
        // heading would leave the route pointing at nothing, and nothing held the two together (MASTER-PLAN-2026-11-17
        // R13a, V5). Every quoted name in a stop's "Do this" that opens a document is a heading of that document.
        string stops = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "reviewStops.ts"));
        var quoted = StopDoThis().Matches(stops)
            .SelectMany(m => Quoted().Matches(m.Groups[2].Value).Select(q => (Slug: m.Groups[1].Value, Heading: q.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(quoted);
        Assert.All(quoted, q =>
        {
            Assert.True(DocumentationCatalog.Files.TryGetValue(q.Slug, out string? file), $"{q.Slug} is not a document the catalogue serves");
            string[] headings = [.. File.ReadAllLines(Path.Combine(Repo.Root(), file!)).Where(l => l.StartsWith('#')).Select(l => l.TrimStart('#').Trim())];
            Assert.Contains(q.Heading, headings);
        });
    }
}
