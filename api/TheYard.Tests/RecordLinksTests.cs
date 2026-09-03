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
/// the links in fifty-one records, and nothing checked that they landed.</para>
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
            // follow redirects. The one instance of this in fifty-one records
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
}
