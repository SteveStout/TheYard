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
}
