using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The review route's tour (ADR: Ten minutes, seven stops; ADR: The review route gives a reason before it asks, the
/// addendum on one unit): read from the one list the page draws, seven stops, each opening an address this site
/// serves (a document the API's catalogue serves, a view, the home page or the resume) with its cell's one line; and
/// the section every "see the proof" button lands on is a heading of the page it opens.
/// </summary>
public sealed partial class ReviewTourTests
{
    [GeneratedRegex(@"address: '([^']*)',[\s\S]*?line: '([^']*)',")]
    private static partial Regex StopLine();

    [GeneratedRegex(@"export const PROOF = \{ doc: '([^']*)', anchor: '([^']*)' \}")]
    private static partial Regex Proof();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotSlug();

    [Fact]
    public void Every_tour_stop_opens_an_address_this_site_serves_and_carries_its_one_line()
    {
        string stops = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "reviewStops.ts"));
        var pairs = StopLine().Matches(stops).Select(m => (Address: m.Groups[1].Value, Line: m.Groups[2].Value)).ToList();

        Assert.Equal(7, pairs.Count);
        Assert.Equal(7, pairs.Select(p => p.Address).Distinct(StringComparer.Ordinal).Count());
        Assert.All(pairs, p =>
        {
            Assert.StartsWith("/", p.Address, StringComparison.Ordinal);
            if (p.Address.StartsWith("/?doc=", StringComparison.Ordinal))
            {
                string slug = p.Address["/?doc=".Length..];
                Assert.True(DocumentationCatalog.Files.ContainsKey(slug), $"{slug} is not a document the catalogue serves");
            }
            else
            {
                Assert.True(
                    p.Address is "/" or "/resume?pdf=1" || p.Address.StartsWith("/?view=", StringComparison.Ordinal),
                    $"{p.Address} is not an address this site serves");
            }
            Assert.EndsWith(".", p.Line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_section_the_proof_lands_on_is_a_heading_of_the_page_it_opens()
    {
        // "See the proof in 2 minutes", the short path's Quality stop and the mirror's slip line all land on "The slip,
        // and the turn"; a rewrite of the page that dropped or renamed that heading would leave them pointing at nothing.
        string verdict = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "reviewVerdict.ts"));
        var proof = Proof().Match(verdict);
        Assert.True(proof.Success, "reviewVerdict.ts names the proof's document and section");
        string slug = proof.Groups[1].Value;
        Assert.True(DocumentationCatalog.Files.TryGetValue(slug, out string? file), $"{slug} is not a document the catalogue serves");
        var anchors = File.ReadAllLines(Path.Combine(Repo.Root(), file!))
            .Where(line => line.StartsWith('#'))
            .Select(line => NotSlug().Replace(line.TrimStart('#').Trim().ToLowerInvariant(), "-").Trim('-'));
        Assert.Contains(proof.Groups[2].Value, anchors);
    }
}
