using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The shape every decision record keeps (ADR: The rules a change has to pass).
///
/// <para>Seventy-four records held the same shape by habit: a title the index
/// can read, a status line saying what became of the decision, and a Files
/// section pointing at the code it decided about. The seventy-fourth broke it,
/// thirteen days after the convention settled, with a bold status line in a
/// date format no other record uses. Nothing failed, because nothing was
/// looking, and it was found by a review rather than by the build.</para>
///
/// <para>A record is the one artifact here that is read by somebody who has
/// never seen the code, so the cost of drift is paid by exactly the reader this
/// project is written for.</para>
/// </summary>
public class RecordShapeTests
{
    /// <summary>
    /// The four records written before the titled convention settled. They open
    /// "# ADR-001:" rather than "# ADR:", which is how the index reads them and
    /// how every citation of them is written, by number. Renaming them now
    /// would rewrite four titles to make history look tidier than it was, which
    /// is the opposite of what a record is for.
    /// </summary>
    private static readonly string[] Numbered =
    [
        "ADR-001-front-door-origin.md",
        "ADR-002-docker-packaging.md",
        "ADR-003-azure-naming.md",
        "ADR-004-deployment-pivots.md",
    ];

    /// <summary>What a record is allowed to say became of it.</summary>
    private static readonly string[] Words = ["accepted", "proposed", "written", "roughed"];

    // #region shape
    private static IEnumerable<(string Name, string[] Lines)> Records()
    {
        foreach (string path in Directory
            .EnumerateFiles(Path.Combine(Repo.Root(), "docs"), "ADR-*.md")
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return (Path.GetFileName(path), File.ReadAllLines(path));
        }
    }

    private static void NoneOf(List<string> wrong) =>
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));

    /// <summary>
    /// The index, the sidebar and every citation read a record's title off its
    /// first line, so a record whose first line is something else is a record
    /// nothing can name.
    /// </summary>
    [Fact]
    public void Every_record_opens_with_its_title_the_way_the_index_reads_it()
    {
        var wrong = new List<string>();

        foreach ((string name, string[] lines) in Records())
        {
            string first = lines.Length == 0 ? string.Empty : lines[0].Trim();
            bool titled = Regex.IsMatch(first, @"^# ADR: \S");
            bool numbered = Numbered.Contains(name) && Regex.IsMatch(first, @"^# ADR-\d{3}: \S");

            if (!titled && !numbered)
            {
                wrong.Add($"{name} opens '{first}' and a record opens '# ADR: ' and its title");
            }
        }

        NoneOf(wrong);
    }

    /// <summary>
    /// A record says what became of the decision, in a line that starts with
    /// Status and sits above the first section, so the reader learns whether
    /// this is still true before reading the argument for it. The vocabulary is
    /// small on purpose: four words across seventy-five records, and a fifth is
    /// a decision about the record set rather than a typo to let through.
    /// </summary>
    [Fact]
    public void Every_record_says_what_became_of_it_before_it_argues_anything()
    {
        var wrong = new List<string>();

        foreach ((string name, string[] lines) in Records())
        {
            int status = Array.FindIndex(lines, line => line.StartsWith("Status:", StringComparison.Ordinal));
            int section = Array.FindIndex(lines, line => line.StartsWith("## ", StringComparison.Ordinal));

            if (status < 0)
            {
                wrong.Add($"{name} carries no line beginning 'Status:'");
                continue;
            }

            if (section >= 0 && status > section)
            {
                wrong.Add($"{name} states its status at line {status + 1}, below the first section at line {section + 1}");
            }

            string word = lines[status]["Status:".Length..].TrimStart().Split([' ', ','], 2)[0].ToLowerInvariant();
            if (!Words.Contains(word))
            {
                wrong.Add($"{name} says its status is '{word}', which is not one of: {string.Join(", ", Words)}");
            }
        }

        NoneOf(wrong);
    }

    /// <summary>
    /// Every record ends with the files it decided about, which is the half of
    /// the documentation that takes a reader from the argument to the code.
    /// RecordLinksTests already holds those links to files that exist; this
    /// holds the section itself, because a record with no Files section has no
    /// links for that test to check and passes it in silence. One record did:
    /// the staff review, from 2 September until this was written.
    /// </summary>
    [Fact]
    public void Every_record_ends_with_the_files_it_decided_about()
    {
        var wrong = new List<string>();

        foreach ((string name, string[] lines) in Records())
        {
            int files = Array.FindIndex(lines, line => line.StartsWith("## Files", StringComparison.Ordinal));
            if (files < 0)
            {
                wrong.Add($"{name} has no Files section");
                continue;
            }

            bool linked = lines.Skip(files).Any(line => line.TrimStart().StartsWith("- [", StringComparison.Ordinal));
            if (!linked)
            {
                wrong.Add($"{name} has a Files section with nothing linked under it");
            }
        }

        NoneOf(wrong);
    }
    // #endregion shape

    /// <summary>
    /// The allow-list above is four names, and a name stays on it only while it
    /// is still an exception. If one of those records is ever brought into the
    /// titled form, this fails and the list shrinks, so the exception cannot
    /// outlive the thing it excused. SealedByDefaultTests holds its own
    /// allow-list the same way.
    /// </summary>
    [Fact]
    public void The_allow_list_holds_no_record_that_has_come_into_line()
    {
        string docs = Path.Combine(Repo.Root(), "docs");
        var wrong = new List<string>();

        foreach (string name in Numbered)
        {
            string path = Path.Combine(docs, name);
            if (!File.Exists(path))
            {
                wrong.Add($"{name} is on the allow-list and is not in docs/");
                continue;
            }

            if (File.ReadLines(path).First().Trim().StartsWith("# ADR: ", StringComparison.Ordinal))
            {
                wrong.Add($"{name} reads as a titled record now, so it does not need the allow-list");
            }
        }

        NoneOf(wrong);
    }
}
