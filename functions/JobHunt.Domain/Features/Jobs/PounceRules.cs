using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The pounce gate (pounce1110 step 2b), stricter than the night's picker because no lane reads a role after it: a
/// posting passes when JobsGates passes it read with its text, its remote read is exactly remote, its title is a seat
/// his targeting file names, its company is not on BLACKLIST.md, and no role that passed in the last 30 days has the
/// same company, base title and level. The seat lists are his targeting file's: software engineering, architecture
/// and engineering management in; customer-facing implementation, product, sales, marketing, design, data science,
/// security and the rest out. They hold for the pounce only; the 2 o'clock pick does not read them.
/// </summary>
public static partial class PounceRules
{
    /// <summary>How far back a passed role makes a second one with the same key a duplicate, in days.</summary>
    public const int DuplicateDays = 30;

    /// <summary>The first reason the pounce refuses the role, in the picker's words, or null when it passes.</summary>
    /// <param name="verdict">JobsGates' verdict on the posting, read with its text.</param>
    /// <param name="blacklist">The company names BLACKLIST.md blocks whole.</param>
    /// <param name="passed">The duplicate keys of the roles that passed in the last 30 days.</param>
    public static string? Gate(Verdict verdict, IReadOnlyList<string> blacklist, IReadOnlySet<string> passed)
    {
        var c = verdict.Candidate;
        return verdict.Failed
            ?? (verdict.Remote != "remote" ? verdict.Remote : null)
            ?? (!IsSeat(c.Title) ? "not a seat his targeting file names" : null)
            ?? (blacklist.Any(name => ApplicationCap.Same(name, c.Company)) ? "on the blacklist" : null)
            ?? (passed.Contains(JobsGates.Same(c)) ? $"the same role passed in the last {DuplicateDays} days" : null);
    }

    /// <summary>
    /// A software engineering, architecture or engineering management seat, and none of the functions ruled out. An
    /// engineer, engineering or architect title counts only when it is bare (every other word a level word, "Staff
    /// Engineer") or carries a software word ("Platform Engineer"), because a bare "engineer" also passed civil,
    /// manufacturing and systems engineers and filled the hiring signal with them (jhcode1112 1a, 11/12 card).
    /// </summary>
    public static bool IsSeat(string title) =>
        (Seat().IsMatch(title) || Management().IsMatch(title) || (Engineer().IsMatch(title) && (SoftwareWord().IsMatch(title) || IsBare(title))))
        && !NotSeat().IsMatch(title);

    /// <summary>True when every word of the title besides engineer, engineering and architect is a level word or a numeral.</summary>
    private static bool IsBare(string title) =>
        Word().Matches(title.ToLowerInvariant()).Select(m => m.Value)
            .Where(w => w is not ("engineer" or "engineering" or "architect"))
            .All(w => LevelWords.Contains(w) || w.All(char.IsAsciiDigit));

    private static readonly HashSet<string> LevelWords = ["staff", "principal", "senior", "sr", "lead", "distinguished", "i", "ii", "iii", "iv", "v", "vi"];

    /// <summary>True when the row names a leader seat, read over the Person cell, the Company cell and the first 300 characters of Notes.</summary>
    public static bool IsLeader(string person, string company, string notes) =>
        Leader().IsMatch($"{person} {company} {(notes.Length > 300 ? notes[..300] : notes)}");

    /// <summary>
    /// True when the address carries the person's own name, a part of three letters or more (jsabin@ for Jason Sabin),
    /// and not a shared mailbox under a leader's name (info@, careers@, press@).
    /// </summary>
    public static bool IsNamedAddress(string address, string fullName)
    {
        int at = address.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }
        string local = Letters(address[..at]);
        return fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Letters).Any(name => name.Length >= 3 && local.Contains(name, StringComparison.Ordinal));
    }

    /// <summary>
    /// The companies BLACKLIST.md blocks whole: each table row whose Person cell opens "ALL OF", its Company cell up to
    /// the first parenthesis or comma, and every name the Person cell says it includes. A row for one person, or for
    /// one job and not the company, blocks no company.
    /// </summary>
    public static IReadOnlyList<string> Blacklisted(string file)
    {
        var names = new List<string>();
        foreach (string line in file.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|')))
        {
            string[] cells = line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells.Length < 4 || !cells[1].StartsWith("ALL OF ", StringComparison.Ordinal))
            {
                continue;
            }
            names.Add(cells[3].Split('(', ',')[0].Trim());
            if (Includes().Match(cells[1]) is { Success: true } includes)
            {
                names.AddRange(Splitter().Split(includes.Groups[1].Value).Select(n => n.Trim()).Where(n => n.Length > 0));
            }
        }
        return [.. names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string Letters(string text) => new([.. text.ToLowerInvariant().Where(ch => ch is >= 'a' and <= 'z')]);

    [GeneratedRegex(@"\b(software|developer|technical lead|tech lead|application development|software development)\b|\.net\b|c#|\bAI\b", RegexOptions.IgnoreCase)]
    private static partial Regex Seat();

    [GeneratedRegex(@"\b(engineer|engineering|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Engineer();

    [GeneratedRegex(@"\b(software|platform|back[- ]?end|full[- ]?stack|web|applications?|cloud|data platform|site reliability|devops)\b|\.net\b|c#", RegexOptions.IgnoreCase)]
    private static partial Regex SoftwareWord();

    // His targeting file names these seats outright: Solutions and Technical Architect, Engineering Manager, and the
    // engineering leaders the leads list aims at.
    [GeneratedRegex(@"\b((technical|solutions?) architect|engineering manager|manager,? (of )?(software )?engineering|(head|director|vp|vice president) of (software )?engineering)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Management();

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\b(sales|pre-?sales|solutions? (engineer|consultant)|forward deployed|implementation|customer|support|account manager|engagement|enablement|product (manager|management|designer|design|marketing|owner|analyst)|marketing|designer|creative|recruit\w*|talent|business development|project manager|program manager|scientist|bioinformatics|data (analyst|scientist|science|engineer\w*)|analyst|security|devsecops|iam|identity|compliance|sip|network engineer|hardware|mechanical|electrical|firmware|embedded|qa|quality|test\w*|sdet|it engineer|systems? administrator|help ?desk|finance|accounting|legal|operations manager|revenue)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotSeat();

    [GeneratedRegex(@"\b(S?VP|EVP|vice president|head of|director|CTO|chief \w+ officer|engineering manager|manager,? (of )?(software|engineering)|hiring manager|founder|CEO)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Leader();

    [GeneratedRegex(@"includes ([^;)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Includes();

    [GeneratedRegex(@",|\band\b")]
    private static partial Regex Splitter();
}
