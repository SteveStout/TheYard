using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Watch;

/// <summary>
/// The dark-lane alarm's reader of state.md: each CLOSED row in the window, a 6 AM row split into its lanes, a pounce row
/// judged by the DONE note it names, each judged as check_empty_runs.py judges it (verdict, word for word).
/// </summary>
public static partial class DarkLanes
{
    /// <summary>One run of one lane, as its row or note words it.</summary>
    /// <param name="Day">The Central day of its AID stamp.</param>
    /// <param name="Lane">Its key.</param>
    /// <param name="Verdict">"done", "empty", "quiet", "held" (a campaign pass the no-email-day gate held), or null (another lane's turn, a dry run, no count).</param>
    /// <param name="Words">Its words, cut to 200.</param>
    public sealed record Run(DateOnly Day, string Lane, string? Verdict, string Words);

    /// <summary>The DONE notes the pounce rows of the window name, which the reader fetches before <see cref="Lines"/>.</summary>
    /// <param name="state">state.md.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<string> NotesWanted(string state, DateOnly today) =>
        [.. Rows(state, today).Where(r => Pounce().IsMatch(r.Lane)).Select(r => NotePath(r.Words)).OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>Every run of every lane in the window: a 6 AM row split into its lanes, a pounce row judged by its note.</summary>
    /// <param name="state">state.md.</param>
    /// <param name="notes">The pounce rows' DONE notes by path.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<Run> Runs(string state, IReadOnlyDictionary<string, string> notes, DateOnly today)
    {
        var found = new List<Run>();
        foreach (var (day, lane, rest) in Rows(state, today))
        {
            if (lane.StartsWith("server6am", StringComparison.Ordinal))
            {
                string[] parts = SubLane().Split(rest);
                for (int i = 1; i + 1 < parts.Length; i += 2)
                {
                    found.Add(new Run(day, $"server6am {parts[i]}", Verdict(parts[i + 1], outbound: true), Cut(parts[i + 1])));
                }
            }
            else if (Leaders().IsMatch(lane))
            {
                found.Add(new Run(day, "leaders", Verdict(rest, outbound: true), Cut(rest)));
            }
            else if (Pounce().IsMatch(lane))
            {
                string? verdict = Verdict(rest, outbound: true);
                if (verdict is null && NotePath(rest) is { } path && notes.TryGetValue(path, out string? note))
                {
                    verdict = Verdict(note.Length > 4000 ? note[..4000] : note, outbound: true);
                }
                found.Add(new Run(day, "pounce", verdict, Cut(rest)));
            }
        }
        return found;
    }

    /// <summary>check_empty_runs.verdict, word for word: a send, a dry run or an expired sign-in (not run), work due and none done, nothing due, a hold.</summary>
    /// <param name="text">A run's words.</param>
    /// <param name="outbound">Whether the lane sends, so its own hold is work that was due.</param>
    public static string? Verdict(string text, bool outbound)
    {
        if (Did().IsMatch(text))
        {
            return "done";
        }
        if (Dry().IsMatch(text) || SignInExpired().IsMatch(text))
        {
            return null;
        }
        if (EmptySent().IsMatch(text) || EmptyLeaders().IsMatch(text) || LeftOut().IsMatch(text))
        {
            return "empty";
        }
        if (Quiet().IsMatch(text))
        {
            return "quiet";
        }
        return outbound && Held().IsMatch(text) ? (Yielded().IsMatch(text) ? null : "empty") : null;
    }

    private static IEnumerable<(DateOnly Day, string Lane, string Words)> Rows(string state, DateOnly today)
    {
        string[] lines = (state ?? string.Empty).Split('\n');
        DateOnly start = today.AddDays(-Window);
        for (int i = 0; i < lines.Length; i++)
        {
            var m = Close().Match(lines[i].TrimEnd('\r'));
            if (!m.Success)
            {
                continue;
            }
            var stamp = lines.Skip(i + 1).Take(3).Select(l => Aid().Match(l)).FirstOrDefault(a => a.Success);
            if (stamp is null || !DateOnly.TryParseExact(stamp.Groups[1].Value, "yyyyMMdd", Inv, DateTimeStyles.None, out var day) || day < start || day > today)
            {
                continue;
            }
            yield return (day, m.Groups[1].Value, m.Groups[2].Value);
        }
    }

    private static string? NotePath(string rest)
    {
        var m = DonePath().Match(rest);
        if (!m.Success)
        {
            return null;
        }
        string path = m.Groups[1].Value.TrimEnd('.', '|', ',');
        path = path.StartsWith("J:\\", StringComparison.Ordinal) ? path[3..] : path.StartsWith("/mnt/j/", StringComparison.Ordinal) ? path[7..] : path;
        return path.Replace('\\', '/');
    }

    private static string Cut(string words)
    {
        string t = words.Trim();
        t = t.EndsWith("| CLOSED |", StringComparison.Ordinal) ? t[..^"| CLOSED |".Length].TrimEnd() : t;
        return t.Length > 200 ? t[..200] : t;
    }

    [GeneratedRegex(@"^## LANE: (\S+) CLOSED\b(.*)$")]
    private static partial Regex Close();

    [GeneratedRegex(@"<!-- AID:(\d{8})-")]
    private static partial Regex Aid();

    [GeneratedRegex(@"\b(feed|invites|messages|network|apply|commentline): ")]
    private static partial Regex SubLane();

    [GeneratedRegex(@"^leaders(?:-20\d{6})?$")]
    private static partial Regex Leaders();

    [GeneratedRegex(@"^pounce-\d+$")]
    private static partial Regex Pounce();

    [GeneratedRegex(@"\b([1-9]\d*) (?:sent verified|leaders written|of \d+ (?:invites )?sent)|\b([1-9]\d*) queued\b")]
    private static partial Regex Did();

    [GeneratedRegex(@"\bdry\b")]
    private static partial Regex Dry();

    // The claude CLI's sign-in, not the lane: "not run, sign-in expired", or the CLI's own words on an older build.
    [GeneratedRegex(@"\bsign-in expired\b|Failed to authenticate")]
    private static partial Regex SignInExpired();

    [GeneratedRegex(@"\b0 sent verified of ([1-9]\d*)")]
    private static partial Regex EmptySent();

    [GeneratedRegex(@"\b0 leaders written\b.*?\b([1-9]\d*) skipped")]
    private static partial Regex EmptyLeaders();

    [GeneratedRegex(@"\b0 (?:sent verified |rows read )?of 0\b[^|]*?\b([1-9]\d*) left out")]
    private static partial Regex LeftOut();

    [GeneratedRegex(@"\b0 (?:sent verified |rows read )?of 0\b|\b0 left out\b")]
    private static partial Regex Quiet();

    [GeneratedRegex(@"\bheld\b", RegexOptions.IgnoreCase)]
    private static partial Regex Held();

    [GeneratedRegex(@"\bLIVE on\b|\byield|\bheld by the breaker\b", RegexOptions.IgnoreCase)]
    private static partial Regex Yielded();

    [GeneratedRegex(@"DONE (?:note:? )?(?:at )?(J:\\\S+|/mnt/j/\S+)")]
    private static partial Regex DonePath();
}
