using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Checks the rule for how many matches a search keeps, using a plain list in place of a
/// store: the limit is honoured, the cut-off flag is exact, and the walk stops one match
/// past the limit instead of reading everything.
/// </summary>
public sealed class SearchMatchesTests
{
    private static StoreEntry Entry(string name) => new(Path.Combine("/home", name), EntryKind.File, 1, 0);

    [Fact]
    public void Keeps_matches_up_to_the_limit_and_says_when_there_were_more()
    {
        StoreEntry[] walk = [Entry("a.md"), Entry("b.txt"), Entry("c.md"), Entry("d.md")];
        (List<StoreEntry> kept, bool truncated) = SearchMatches.Take(walk, new NamePattern("*.md"), 2);
        Assert.Equal(["a.md", "c.md"], kept.Select(e => Path.GetFileName(e.Absolute)));
        Assert.True(truncated);
    }

    [Fact]
    public void Exactly_the_limit_is_not_cut_off()
    {
        StoreEntry[] walk = [Entry("a.md"), Entry("b.txt"), Entry("c.md")];
        (List<StoreEntry> kept, bool truncated) = SearchMatches.Take(walk, new NamePattern("*.md"), 2);
        Assert.Equal(2, kept.Count);
        Assert.False(truncated);
    }

    [Fact]
    public void The_walk_stops_one_match_past_the_limit()
    {
        int read = 0;
        IEnumerable<StoreEntry> Walk()
        {
            for (int i = 0; i < 1000; i++)
            {
                read++;
                yield return Entry($"file-{i}.md");
            }
        }
        (List<StoreEntry> kept, bool truncated) = SearchMatches.Take(Walk(), new NamePattern("*.md"), 10);
        Assert.Equal(10, kept.Count);
        Assert.True(truncated);
        Assert.Equal(11, read);
    }
}
