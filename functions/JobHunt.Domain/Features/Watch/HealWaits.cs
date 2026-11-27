using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Watch;

/// <summary>
/// A heal that landed is "landed, waiting on" the task's next real run, never fixed on its own read (the nightly-jobs fix,
/// R1 items 5 and 6, J:\mentor\nightfix1127\PROMPT-NIGHTFIX1127-CODING.md). The dispatcher writes the HEALED.md line
/// (dispatch_heal.landed, from the heal lane's DONE note); the ladder reads it: until that run's time the old failed reading
/// is not counted again (no retry line, no heal, no budget spent); after it, a pass is fixed and a fail reopens the item,
/// its attempts counted again from there. A heal of a red main is outside the day's code-heal budget.
/// </summary>
public static partial class HealWaits
{
    /// <summary>The words of the dispatcher's landed line, before the run it waits on.</summary>
    public const string Landed = "| 4 heal lane | landed, waiting on ";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// The index in HEALED.md of the newest landed line for a signature that names its run, or -1. A line that names none
    /// ("the next run, none named") holds nothing back and never starts the attempts again: a heal that built nothing
    /// lands nothing.
    /// </summary>
    /// <param name="healed">HEALED.md's lines.</param>
    /// <param name="sig">The failure's signature.</param>
    public static int LandedAt(IReadOnlyList<string> healed, string sig)
    {
        for (int i = healed.Count - 1; i >= 0; i--)
        {
            if (healed[i].Contains(Landed, StringComparison.Ordinal) && healed[i].EndsWith($"sig={sig}", StringComparison.Ordinal)
                && When().IsMatch(healed[i][(healed[i].IndexOf(Landed, StringComparison.Ordinal) + Landed.Length)..]))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>The Central time of the run the newest landed line for a signature waits on, or null.</summary>
    /// <param name="healed">HEALED.md's lines.</param>
    /// <param name="sig">The failure's signature.</param>
    public static DateTime? Run(IReadOnlyList<string> healed, string sig)
    {
        int at = LandedAt(healed, sig);
        if (at < 0 || !DateTime.TryParseExact(healed[at].Length > 18 ? healed[at][2..18] : string.Empty, "yyyy-MM-dd HH:mm", Inv, DateTimeStyles.None, out DateTime written))
        {
            return null;
        }
        Match m = When().Match(healed[at][(healed[at].IndexOf(Landed, StringComparison.Ordinal) + Landed.Length)..]);
        if (!m.Success)
        {
            return null;
        }
        int month = int.Parse(m.Groups[1].Value, Inv), day = int.Parse(m.Groups[2].Value, Inv);
        int year = written.Year + (month < written.Month - 6 ? 1 : 0);
        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateTime(year, month, day, int.Parse(m.Groups[3].Value, Inv), int.Parse(m.Groups[4].Value, Inv), 0, DateTimeKind.Unspecified)
            : null;
    }

    /// <summary>Whether a failing reading is the old one a landed heal still waits on: its run's time is ahead.</summary>
    /// <param name="healed">HEALED.md's lines.</param>
    /// <param name="sig">The failure's signature.</param>
    /// <param name="local">The Central time now.</param>
    public static bool Waiting(IReadOnlyList<string> healed, string sig, DateTime local) => Run(healed, sig) is { } run && local < run;

    /// <summary>Why a product reading never opens a heal: it is a number the product has to earn, not a fault to fix.</summary>
    public const string ProductReading = "a product reading, not a fault: product item, January";

    /// <summary>
    /// Whether a reading is a product number rather than a fault: W11, the review route's second action (WatchBeats.Review,
    /// "below it is a January item, never a heal"; ADR-139). It keeps reading FAIL with its real number, and its line goes to
    /// NOT-WORKING.md, but no heal lane is opened and no budget is spent on it.
    /// </summary>
    /// <param name="r">The reading.</param>
    public static bool Product(WatchResult r) => r.Test == "W11";

    /// <summary>Whether a reading is a red main: W9 on a CI failure, whose heal is outside the day's code-heal budget.</summary>
    /// <param name="r">The reading.</param>
    public static bool RedMain(WatchResult r) => r.Test == "W9" && r.Number.Contains("CI failure", StringComparison.Ordinal);

    /// <summary>
    /// The lines a tick adds once a landed heal's run is past: "5 fixed" when its reading passes, "4 reopened" when it fails,
    /// each once after the landed line.
    /// </summary>
    /// <param name="results">The tick's readings.</param>
    /// <param name="healed">HEALED.md's lines.</param>
    /// <param name="local">The Central time now.</param>
    public static IReadOnlyList<string> Steps(IReadOnlyList<WatchResult> results, IReadOnlyList<string> healed, DateTime local)
    {
        var lines = new List<string>();
        foreach (WatchResult r in results.Where(r => r.Pass is not null))
        {
            string task = $"{r.Test} {r.Name}", sig = $"watch:{HealLadder.Key(task)}";
            int at = LandedAt(healed, sig);
            if (at < 0 || Run(healed, sig) is not { } run || local < run
                || healed.Skip(at + 1).Any(l => l.EndsWith($"sig={sig}", StringComparison.Ordinal) && (l.Contains("| 5 fixed |", StringComparison.Ordinal) || l.Contains("| 4 reopened |", StringComparison.Ordinal))))
            {
                continue;
            }
            string ran = run.ToString("ddd MM/dd HH:mm", Inv);
            string step = r.Pass == true ? "5 fixed" : "4 reopened", word = r.Pass == true ? "passed" : "failed";
            lines.Add($"- {local.ToString("yyyy-MM-dd HH:mm", Inv)} CST | {task} | {$"{r.Number} ({r.Source})".Replace('|', '/')} | {step} | the run of {ran} CST {word}; sig={sig}");
        }
        return lines;
    }

    [GeneratedRegex(@"(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\w*\s+(\d{1,2})/(\d{1,2})\s+(\d{1,2}):(\d{2})")]
    private static partial Regex When();
}
