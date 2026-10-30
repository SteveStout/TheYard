using System.Text.RegularExpressions;

namespace TheYard.Rules;

// #region history-rule
/// <summary>
/// One rule, one body, compiled into both test projects by a link (ADR-075, the addendum of 1.0.4.70):
/// a production comment says what, how and why on its own, so nothing in it stands in for a reason. The
/// thing that stands in for a reason is provenance: when the code was written, who asked for it, which
/// version shipped it, which review found it. A reader three months from now has none of that and needs
/// none of it; what they need is the sentence it replaced.
/// </summary>
/// <remarks>
/// <para>Two things make this rule harder than a list of forbidden words, and both are in it.</para>
/// <para>The first is that a date can be the subject rather than the history. A comment that names the
/// wire format a function parses has to show it, and showing it means writing a date, so a date inside
/// backticks or quotes is an example of a format and not a note about the past. Those spans come out
/// before the rule reads the line, and the handful the stripper cannot reach are named one by one: a
/// quotation that runs across two source lines, where a quote has no other half to pair with, and a
/// month written in code rather than in prose.</para>
/// <para>The second is that the same word is history in one part of this repository and the subject in
/// another, so the rule is asked in two strengths. The site's own code has no reason to name the person
/// who owns it or a month of the year, so there a bare month and a bare first name are provenance and
/// nothing else. The job hunt's code reads his inbox, his calendar and his tracker, and the clock in it
/// turns on when the clocks change, so there his name and the word November are what the code is about.
/// What is provenance in both is a date, a version number, a named review, and his name beside a date or
/// beside an ask.</para>
/// <para>Held to the same shapes as <c>.github/quality.py</c>'s <c>history_or_name_comments</c>, kept the
/// same deliberately: if the two disagreed, the count would be describing a different codebase from the
/// one the suite refuses to let slip.</para>
/// </remarks>
public static class HistoryRule
{
    /// <summary>
    /// Provenance in any production comment: an ISO date with its day optional, a dated slash date, a day
    /// beside a month name, the owner's name beside an ask, the owner's name within a short reach of a
    /// bare slash date, a bare slash date within a short reach of a word for an instruction, a version
    /// number, and a review named rather than described.
    /// </summary>
    /// <remarks>
    /// A day beside a month name counts only when no four-digit year follows it. A recent date in this
    /// project's own past is written the way a person writes one, "23 Oct" or "7 October", because the
    /// year is this one; a date that spells its year out is naming a fixed point that a comment may
    /// legitimately need, and the one in this repository is the Unix epoch. A bare slash date is never
    /// read alone, because "90/75" is the coverage floor and "1/2" is a half.
    /// </remarks>
    private static readonly Regex Provenance = new(
        @"\b20\d\d-\d\d(-\d\d)?\b"
        + @"|\b\d{1,2}/\d{1,2}/20\d\d\b"
        + @"|\b\d{1,2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\b(?! \d{4})"
        + @"|\b(Steve|he)('s)? (said|asked|named|wants|ruled|word|call)\b"
        + @"|\b(Steve|he|his)\b[^.]{0,40}?\b\d{1,2}/\d{1,2}\b"
        + @"|\b\d{1,2}/\d{1,2}\b[^.]{0,40}?\b(rule|ask|asked|said|named|wants|ruled|call)\b"
        + @"|\b1\.0\.\d+\.\d+\b"
        + @"|the review of|the staff review|the tweaks pass|the self-review",
        RegexOptions.Compiled);

    /// <summary>
    /// The month names and the owner's name on their own, which are provenance in the site's own code and
    /// the subject in the job hunt's. May is only a month beside a day, because it is also a verb.
    /// </summary>
    private static readonly Regex SiteOnly = new(
        @"\b(January|February|March|April|June|July|August|September|October|November|December)\b"
        + @"|\bMay \d|\d May\b|\bSteve\b",
        RegexOptions.Compiled);

    /// <summary>
    /// The format examples the stripper cannot reach: a quotation that runs across two source lines, so a
    /// quote cannot be paired with its other half, and a month written in code rather than in prose. The
    /// chart's axis labels, a day as the cost card writes it, the months as a reader's pattern lists them,
    /// a UTC day the chart reads, the opening line of a decision record as the document layout matches it,
    /// and the run line the share's log holds. Each is a file name and a fragment that line contains, which
    /// is narrower than a file name alone and survives the line moving.
    /// </summary>
    private static readonly (string File, string Fragment)[] FormatExamples =
    [
        ("activityChart.ts", "`2026-09-13`"),
        ("costWords.ts", "'September',"),
        ("costWords.ts", "\"30 September\""),
        ("Recommendations.cs", "September|October|November|December"),
        ("docLayout.ts", "shipped as 1.0.0.21."),
        ("dayMarks.ts", "\"Mon 5 Oct, Fri 9,"),
        ("dayMarks.ts", "Mon 12, Fri 16\""),
        ("RunLine.cs", "process:Merger outcome=success"),
    ];

    /// <summary>
    /// The part of a line that is a comment: the whole line when it opens with one, else what follows a
    /// trailing one, and nothing when the line is only code.
    /// </summary>
    /// <param name="line">The source line.</param>
    public static string CommentOf(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*')
            || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("{/*", StringComparison.Ordinal))
        {
            return line;
        }

        int trailing = line.IndexOf(" // ", StringComparison.Ordinal);
        return trailing >= 0 ? line[trailing..] : "";
    }

    /// <summary>A comment with its quoted and backticked examples taken out, so a format is not read as a date.</summary>
    /// <param name="comment">The comment text.</param>
    public static string Unquoted(string comment) => Regex.Replace(comment, "`[^`]*`|\"[^\"]*\"", "");

    /// <summary>
    /// Whether this line is one of the named lines whose quotation runs across two source lines, which the
    /// stripper cannot reach.
    /// </summary>
    /// <param name="path">The file's path, of which only the name is read.</param>
    /// <param name="line">The source line.</param>
    public static bool IsFormatExample(string path, string line) =>
        FormatExamples.Any(allowed =>
            Path.GetFileName(path) == allowed.File && line.Contains(allowed.Fragment, StringComparison.Ordinal));

    /// <summary>
    /// Whether a line's comment leans on provenance, asked at the strength the area deserves.
    /// </summary>
    /// <param name="path">The file's path, read for the named format examples.</param>
    /// <param name="line">The source line.</param>
    /// <param name="siteStrength">
    /// True for the site's own code, where a bare month and a bare first name are provenance too; false for
    /// the job hunt's code and the sample app, where both are the subject.
    /// </param>
    public static bool LeansOnHistory(string path, string line, bool siteStrength)
    {
        if (IsFormatExample(path, line))
        {
            return false;
        }

        string comment = Unquoted(CommentOf(line));
        return Provenance.IsMatch(comment) || (siteStrength && SiteOnly.IsMatch(comment));
    }

    /// <summary>
    /// Every line in one file that leans on provenance, as its line number counted from one the way an
    /// editor counts, and the trimmed text, so a failure names the file and the line rather than a count.
    /// </summary>
    /// <param name="path">The file's path, read for the named format examples.</param>
    /// <param name="lines">The file's lines.</param>
    /// <param name="siteStrength">True for the site's own code.</param>
    public static List<(int Line, string Text)> HistoryIn(string path, IReadOnlyList<string> lines, bool siteStrength)
    {
        var found = new List<(int, string)>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (LeansOnHistory(path, lines[i], siteStrength))
            {
                found.Add((i + 1, lines[i].Trim()));
            }
        }

        return found;
    }
}
// #endregion history-rule
