namespace TheYard.Tests;

/// <summary>
/// What this repository says it is, to somebody who opens it rather than the
/// site (ADR: The name, and how a rename was done without losing the history).
///
/// <para>This project began as a submission to somebody else's hiring
/// challenge, in a fork of their repository. Most of that inheritance was
/// cleaned up on the first day. Two files survived: the challenge's own
/// interview brief, sitting at the root of a public repository explaining how a
/// submission would be evaluated, and a set of private working notes describing
/// the cleanup. Both were found on 3 September, by reading the root directory
/// rather than by any check.</para>
/// </summary>
public class PublicSurfaceTests
{
    // #region inheritance
    [Fact]
    public void The_repository_does_not_carry_the_challenge_it_grew_out_of()
    {
        string root = Repo.Root();
        // One record explains where the name came from, on purpose, and is the
        // only place the challenge should be named.
        const string Allowed = "ADR-046-the-name.md";

        var carried = new List<string>();
        foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(path) == Allowed)
            {
                continue;
            }

            if (File.ReadAllText(path).Contains("OPENLANE", StringComparison.OrdinalIgnoreCase))
            {
                carried.Add(Path.GetRelativePath(root, path));
            }
        }

        Assert.True(
            carried.Count == 0,
            $"only {Allowed} should name the challenge this grew out of, and these also do: "
            + string.Join(", ", carried));
    }

    [Fact]
    public void Every_file_that_says_what_this_is_says_the_same_thing()
    {
        string root = Repo.Root();
        // CLAUDE.md described an industrial and farm equipment marketplace for
        // three days after the decision not to rename the domain. It is the
        // file an agent reads first to learn what it is working on, which makes
        // it the worst place in the repository for a wrong sentence.
        foreach (string name in new[] { "README.md", "CLAUDE.md" })
        {
            string text = File.ReadAllText(Path.Combine(root, name));
            Assert.Contains("used-vehicle auction", text, StringComparison.OrdinalIgnoreCase);
        }
    }
    // #endregion inheritance
}
