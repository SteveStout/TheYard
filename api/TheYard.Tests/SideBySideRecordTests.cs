using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The side-by-side record (ADR: SQL Server and Cosmos DB, side by side)
/// promises the same thing from each store next to the other: the same bid at
/// rest, the same write, the same guarantee, one live sample from the
/// relational side and one from the document side in every pair, and a link
/// to every record it draws on. A record that showed one side only, or fell
/// out of pairs as files moved, would still render; this holds the promise
/// as a test, the way ADR-018's numbers and the README's counts are held.
/// </summary>
public class SideBySideRecordTests
{
    private const string Record = "docs/ADR-070-sql-server-and-cosmos-db-side-by-side.md";

    /// <summary>Where the relational side's code and schema live.</summary>
    private static readonly string[] Relational = ["api/TheYard.Infrastructure/", "api/TheYard.Database/"];

    /// <summary>Where the document side's code and container definitions live.</summary>
    private static readonly string[] Document = ["api/TheYard.Infrastructure.Cosmos/", "infra/cosmos/"];

    // #region pairs
    [Fact]
    public void Every_live_sample_in_the_record_has_its_twin_from_the_other_store_next_to_it()
    {
        string markdown = File.ReadAllText(Path.Combine(Repo.Root(), Record));
        // The fence is spelled in two pieces so this file, shown live by a
        // record, does not itself read as a block left unexpanded.
        var paths = Regex.Matches(markdown, "^``" + "`live path=(?<path>\\S+)", RegexOptions.Multiline)
            .Select(match => match.Groups["path"].Value)
            .ToList();

        Assert.True(paths.Count >= 6 && paths.Count % 2 == 0, $"the record shows {paths.Count} samples, and it promises pairs");
        for (int i = 0; i < paths.Count; i += 2)
        {
            Assert.True(
                Relational.Any(root => paths[i].StartsWith(root, StringComparison.Ordinal)),
                $"sample {i + 1} should be the relational side of a pair, not {paths[i]}");
            Assert.True(
                Document.Any(root => paths[i + 1].StartsWith(root, StringComparison.Ordinal)),
                $"sample {i + 2} should be the document side of a pair, not {paths[i + 1]}");
        }
    }

    [Fact]
    public void The_record_links_every_store_record_it_compares_and_its_diagram_has_a_page()
    {
        string markdown = File.ReadAllText(Path.Combine(Repo.Root(), Record));

        // Every ?doc= address in the record is a slug the catalogue serves
        // (ADR: A record with no address).
        var slugs = Regex.Matches(markdown, @"\?doc=(?<slug>[a-z0-9-]+)")
            .Select(match => match.Groups["slug"].Value)
            .Distinct()
            .ToList();
        Assert.True(slugs.Count >= 10, $"only {slugs.Count} records are linked from the comparison");
        var unknown = slugs.Where(slug => !DocsCatalog.Files.ContainsKey(slug)).ToList();
        Assert.True(unknown.Count == 0, "linked as records, served by nothing: " + string.Join(", ", unknown));

        foreach (string slug in new[]
        {
            "adr-sql-server", "adr-data-first", "adr-sql-visible",
            "adr-partition-key", "adr-second-store", "adr-accounts-documents", "adr-store-visible",
            "adr-measuring-stores", "adr-proof", "adr-cosmos-explained",
        })
        {
            Assert.Contains(slug, slugs);
        }

        Assert.True(DocsCatalog.Diagrams.ContainsKey("sql-vs-cosmos"), "the side-by-side drawing should open on its own page");
        Assert.Contains("/api/docs/diagrams/sql-vs-cosmos", markdown);
        Assert.Contains("docs/images/sql-vs-cosmos.png", markdown);
    }
    // #endregion pairs
}
