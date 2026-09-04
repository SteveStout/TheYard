using System.Text.RegularExpressions;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// Every record ends with a Files section linking at the code it decided about,
/// and those links are the main way a reader gets from a decision to the thing
/// it decided. A link that 404s is worse than no link: it says the record has
/// not been read since whatever moved.
///
/// <para>This exists because it was needed. A record written today linked to
/// `docs/ADR-037-accounts-and-per-user-bids.md`, which has never existed; the
/// file is `docs/ADR-037-accounts.md`. It was caught by eye, which is not a
/// method. The rename of every project the same day moved 208 files and rewrote
/// the links in every record, and nothing checked that they landed.</para>
/// </summary>
public class RecordLinksTests
{
    // #region links
    [Fact]
    public void Every_repository_link_in_every_document_points_at_something_that_exists()
    {
        string root = Repo.Root();
        var broken = new List<string>();
        int checkedLinks = 0;

        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md")
            .Concat([Path.Combine(root, "README.md")]))
        {
            string text = File.ReadAllText(path);
            // Only links into this repository at main. External URLs are
            // somebody else's uptime and are not this test's business.
            //
            // blob and tree are both checked, and which one is used matters:
            // GitHub serves a directory under tree and a file under blob, and
            // gets there from the wrong one by redirecting. So the wrong form
            // works, quietly, until it is copied into somewhere that does not
            // follow redirects. The one instance of this across the records
            // was a directory linked as a blob.
            foreach (Match match in Regex.Matches(
                text, @"https://github\.com/SteveStout/TheYard/(blob|tree)/main/([^\)\s""#]+)"))
            {
                checkedLinks++;
                string kind = match.Groups[1].Value;
                string target = match.Groups[2].Value;
                string full = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar));
                bool isFile = File.Exists(full);
                bool isDirectory = Directory.Exists(full);

                if (!isFile && !isDirectory)
                {
                    broken.Add($"{Path.GetFileName(path)} -> {target} (nothing there)");
                }
                else if (isFile && kind == "tree")
                {
                    broken.Add($"{Path.GetFileName(path)} -> {target} (a file linked as a tree)");
                }
                else if (isDirectory && kind == "blob")
                {
                    broken.Add($"{Path.GetFileName(path)} -> {target} (a directory linked as a blob)");
                }
            }
        }

        Assert.True(checkedLinks > 100, $"only {checkedLinks} repository links found, which suggests this stopped matching");
        Assert.True(broken.Count == 0, "these links point at files that are not there:\n  " + string.Join("\n  ", broken));
    }

    [Fact]
    public void Every_record_the_sidebar_offers_has_a_file_and_every_file_is_offered()
    {
        string root = Repo.Root();
        var served = DocsCatalog.Files.Values
            .Where(path => path.StartsWith("docs/ADR-", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToHashSet(StringComparer.Ordinal);

        var onDisk = Directory.EnumerateFiles(Path.Combine(root, "docs"), "ADR-*.md")
            .Select(Path.GetFileName)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        // Both directions. A record nobody can open from the site is a record
        // that only exists for whoever clones the repository, and a catalogue
        // entry with no file behind it is a 500 waiting for somebody to click.
        Assert.True(
            served.SetEquals(onDisk),
            "on disk but not served: [" + string.Join(", ", onDisk.Except(served))
            + "]; served but not on disk: [" + string.Join(", ", served.Except(onDisk)) + "]");
    }

    [Fact]
    public void Record_numbers_run_without_a_gap()
    {
        string root = Repo.Root();
        int[] numbers = Directory.EnumerateFiles(Path.Combine(root, "docs"), "ADR-*.md")
            .Select(path => Regex.Match(Path.GetFileName(path), @"^ADR-(\d+)"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .OrderBy(number => number)
            .ToArray();

        Assert.NotEmpty(numbers);
        // A gap means a record was deleted, and a record that was decided and
        // then deleted is exactly the thing this index exists to keep.
        var gaps = Enumerable.Range(numbers[0], numbers[^1] - numbers[0] + 1).Except(numbers).ToArray();
        Assert.True(gaps.Length == 0, "these record numbers are missing: " + string.Join(", ", gaps));
    }
    // #endregion links

    // #region the other direction
    /// <summary>
    /// The links above run from a record to the code it decided about. These
    /// run the other way.
    ///
    /// <para>Comments here cite the record that decided the thing they are
    /// explaining, by its title, as "(ADR: The search index)", and there are
    /// more of those than there are links in the records. Nothing checked any
    /// of them. Five were wrong when this was written: two named a record that
    /// does not exist, one cited a section heading as though it were a title,
    /// and two were near misses that a reader would not notice, "Search index"
    /// for "The search index" and "Edge economics" for "Edge deploy
    /// economics".</para>
    ///
    /// <para>A wrong citation is worse than none in the same way a broken link
    /// is: it tells a reader there is a record to go and read, and the reader
    /// who goes looking finds nothing and concludes the documentation is
    /// decorative.</para>
    /// </summary>
    // Built from two halves for the same reason HouseVoiceTests writes an em
    // dash as an escape: this file is one of the files scanned, and a citation
    // pattern written whole would match itself and then fail to resolve.
    private static readonly Regex NamedReference =
        new(@"\(ADR" + @": ([^)]+)\)", RegexOptions.Compiled);

    /// <summary>The four earliest records are cited by number, which is how
    /// they were written before the titled convention settled.</summary>
    private static readonly Regex NumberedReference = new(@"\(ADR-(\d{3})\b", RegexOptions.Compiled);

    /// <summary>
    /// A citation with the comment markers taken off. A reference wraps across
    /// lines inside a doc comment, so the markers have to go before the
    /// whitespace is collapsed or half the titles arrive with a "///" in the
    /// middle of them. Only at the start of a line, so a URL keeps its slashes.
    /// </summary>
    private static string WithoutCommentMarkers(string source) =>
        Regex.Replace(
            Regex.Replace(source, @"^[ \t]*(///?|\*)[ \t]?", " ", RegexOptions.Multiline),
            @"\s+",
            " ");

    /// <summary>Every record's title, taken from its own first line.</summary>
    private static List<string> RecordTitles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "docs"), "ADR-*.md")
            .Select(path => File.ReadLines(path).First().Trim())
            .Where(heading => heading.StartsWith("# ADR: ", StringComparison.Ordinal))
            .Select(heading => heading["# ADR: ".Length..].Trim())
            .ToList();

    /// <summary>
    /// What counts as naming a record: the title itself, or the title cut short
    /// at a word or a comma, which is how a comment cites "Program.cs,
    /// explained" for a record whose full title ends "for a new developer".
    ///
    /// <para>It has to be unambiguous. "The" begins twenty of these titles and
    /// is not a citation, so a reference that names more than one record fails
    /// exactly like one that names none.</para>
    /// </summary>
    private static List<string> RecordsNamedBy(string reference, List<string> titles) =>
        titles
            .Where(title =>
                string.Equals(title, reference, StringComparison.Ordinal)
                || title.StartsWith(reference + " ", StringComparison.Ordinal)
                || title.StartsWith(reference + ",", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// A citation may add its own clause after the title: "(ADR: Reviewing my
    /// own work, which caught the same defect one buffer over)". The whole
    /// string is tried first, so a title that itself contains a comma is
    /// matched in full before anything is thrown away.
    /// </summary>
    private static List<string> Resolve(string reference, List<string> titles)
    {
        var whole = RecordsNamedBy(reference, titles);
        if (whole.Count == 1)
        {
            return whole;
        }

        int comma = reference.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? whole : RecordsNamedBy(reference[..comma].Trim(), titles);
    }

    [Fact]
    public void Every_record_a_comment_cites_by_name_is_a_record_that_exists()
    {
        string root = Repo.Root();
        var titles = RecordTitles(root);
        var wrong = new List<string>();
        int cited = 0;

        foreach (string path in Repo.FilesWith(".cs", ".ts", ".tsx", ".mjs", ".css"))
        {
            string source = WithoutCommentMarkers(File.ReadAllText(path));
            foreach (Match match in NamedReference.Matches(source))
            {
                string reference = match.Groups[1].Value.Trim();
                cited++;
                var named = Resolve(reference, titles);
                if (named.Count != 1)
                {
                    wrong.Add(
                        $"{Path.GetRelativePath(root, path)} cites '{reference}', which names "
                        + (named.Count == 0 ? "no record" : $"{named.Count} records"));
                }
            }
        }

        Assert.True(cited > 20, $"only {cited} citations were found, so this test is reading the wrong files");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong.Distinct()));
    }

    [Fact]
    public void Every_record_a_comment_cites_by_number_is_a_record_that_exists()
    {
        string root = Repo.Root();
        var onDisk = Directory.EnumerateFiles(Path.Combine(root, "docs"), "ADR-*.md")
            .Select(path => Regex.Match(Path.GetFileName(path), @"^ADR-(\d+)").Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var wrong = new List<string>();
        int cited = 0;

        foreach (string path in Repo.FilesWith(".cs", ".ts", ".tsx", ".mjs", ".css"))
        {
            foreach (Match match in NumberedReference.Matches(File.ReadAllText(path)))
            {
                cited++;
                if (!onDisk.Contains(match.Groups[1].Value))
                {
                    wrong.Add($"{Path.GetRelativePath(root, path)} cites ADR-{match.Groups[1].Value}, which is not on disk");
                }
            }
        }

        Assert.True(cited > 5, $"only {cited} numbered citations were found");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong.Distinct()));
    }
    // #endregion the other direction
}
