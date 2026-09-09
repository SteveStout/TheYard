using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// Every live block in every served document renders as code, from this
/// checkout and from the image (ADR: Live code samples, the addendum on
/// coverage; ADR: The second manifest).
///
/// <para>A block that renders a note instead is documentation that does not
/// work, and until 2026-09-09 nothing checked. The measuring record named a
/// file under <c>scripts/</c>, which was outside the expander's roots, and
/// both live sites showed "Sample unavailable" where the method should have
/// been, for a day, while every gate stayed green: the expander's answer to a
/// bad path is a sentence and not an error, by design, so the gate never saw
/// it. This class turns the sentence into a failure, twice over: once for the
/// paths and regions as this checkout has them, and once for the Dockerfile,
/// because a file that is not copied into the runtime stage is "not in this
/// build" on the live site and present on every developer's machine.</para>
/// </summary>
public class LiveSampleCoverageTests
{
    // #region coverage
    /// <summary>The note the expander renders in place of a sample it cannot show.</summary>
    private const string Unavailable = "*Sample unavailable:";

    /// <summary>
    /// Every live block in every served document, as (document, path, region).
    /// The fence is spelled in two pieces so that this file, shown live by a
    /// record, does not itself read as a block left unexpanded.
    /// </summary>
    private static List<(string Document, string Path, string Region)> Blocks(string root)
    {
        var fence = new Regex("^``" + "`live[ \\t]+path=(?<path>\\S+)[ \\t]+region=(?<region>\\S+)", RegexOptions.Multiline);
        var blocks = new List<(string, string, string)>();
        foreach (string relative in DocsCatalog.Files.Values.Distinct().OrderBy(file => file, StringComparer.Ordinal))
        {
            string markdown = File.ReadAllText(Path.Combine(root, relative));
            foreach (Match match in fence.Matches(markdown))
            {
                blocks.Add((relative, match.Groups["path"].Value, match.Groups["region"].Value));
            }
        }
        return blocks;
    }

    [Fact]
    public void Every_live_block_in_every_served_document_renders_as_code_from_this_checkout()
    {
        string root = Repo.Root();
        var blocks = Blocks(root);
        Assert.True(blocks.Count > 50, $"only {blocks.Count} live blocks were found across the catalogue");

        var notes = new List<string>();
        foreach (string relative in DocsCatalog.Files.Values.Distinct())
        {
            string expanded = LiveSamples.Expand(File.ReadAllText(Path.Combine(root, relative)), root, "local");
            foreach (string line in expanded.Split('\n').Where(text => text.Contains(Unavailable, StringComparison.Ordinal)))
            {
                notes.Add($"{relative}: {line.Trim()}");
            }
        }

        Assert.True(
            notes.Count == 0,
            "these blocks render a note where the record promises code:" + Environment.NewLine + string.Join(Environment.NewLine, notes));
    }

    /// <summary>
    /// The runtime stage of the Dockerfile copies an explicit list of sources
    /// for the expander to read. Every path a live block names has to be on
    /// that list, and a glob such as <c>api/TheYard.Api/*.cs</c> covers only
    /// the files beside it, not the ones in a folder below.
    /// </summary>
    [Fact]
    public void Every_file_a_live_block_names_is_copied_into_the_image()
    {
        string root = Repo.Root();
        string dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));
        int runtime = dockerfile.LastIndexOf("\nFROM ", StringComparison.Ordinal);
        Assert.True(runtime >= 0, "the Dockerfile should end with a runtime stage");
        string stage = dockerfile[runtime..];

        // Every source token of every COPY in the runtime stage, except the
        // ones that copy from an earlier stage (those are build outputs, not
        // repository files).
        var sources = new List<string>();
        foreach (Match copy in Regex.Matches(stage, @"^COPY\s+(?<rest>.+)$", RegexOptions.Multiline))
        {
            string[] tokens = copy.Groups["rest"].Value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(token => token.StartsWith("--from=", StringComparison.Ordinal)))
            {
                continue;
            }
            sources.AddRange(tokens.Where(token => !token.StartsWith("--", StringComparison.Ordinal)).SkipLast(1));
        }
        Assert.NotEmpty(sources);

        bool Copied(string path) => sources.Any(source =>
        {
            if (source.Contains('*'))
            {
                int slash = source.LastIndexOf('/');
                string directory = slash < 0 ? "" : source[..(slash + 1)];
                string name = source[(slash + 1)..];
                string pattern = "^" + Regex.Escape(name).Replace("\\*", "[^/]*") + "$";
                return path.StartsWith(directory, StringComparison.Ordinal)
                    && !path[directory.Length..].Contains('/')
                    && Regex.IsMatch(path[directory.Length..], pattern);
            }
            return path == source || path.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal);
        });

        var missing = Blocks(root)
            .Where(block => LiveSamples.IsAllowedPath(block.Path))
            .Where(block => !Copied(block.Path))
            .Select(block => $"{block.Document} names {block.Path}")
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            "the runtime stage of the Dockerfile never copies these, so the live site would say they are not in this build:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }
    // #endregion coverage
}
