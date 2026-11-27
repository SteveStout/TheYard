using System.Globalization;
using System.Text.RegularExpressions;
using JobHunt.Domain.Shared.Clock;

namespace JobHunt.Domain.Features.Watch;

/// <summary>One heal item and its fixture, as the ladder writes them to the share.</summary>
/// <param name="Folder">dispatch/ready when policy.json's heal_mode is on, else dispatch/heal-shadow.</param>
/// <param name="Name">30-heal-&lt;task&gt;-&lt;yyyyMMdd&gt;.md, behind a pounce (10) and a reply draft (12).</param>
/// <param name="Text">The item in its fixed text.</param>
/// <param name="FixtureText">The failing reading, written to dispatch/heal-fixtures/ beside it.</param>
public sealed record HealItem(string Folder, string Name, string Text, string FixtureText)
{
    /// <summary>The fixture's path on the share.</summary>
    public string Fixture => $"dispatch/heal-fixtures/{Name[..^3]}.txt";
}

/// <summary>What one tick's ladder writes: HEALED.md lines, NOT-WORKING.md lines and heal items.</summary>
/// <param name="Healed">One line a step, appended to HEALED.md.</param>
/// <param name="NotWorking">One line a failure the ladder could not fix; the writer skips one the file already holds.</param>
/// <param name="Items">The heal items opened this tick.</param>
public sealed record HealPlan(IReadOnlyList<string> Healed, IReadOnlyList<string> NotWorking, IReadOnlyList<HealItem> Items);

/// <summary>
/// The heal ladder for the watch's failures (the year-end plan's 7b), in the dispatcher's own terms so a watch heal and
/// a check_empty_runs.py heal are one kind of item (J:\dispatch\dispatch_heal.py). The first failing tick logs step 1,
/// the PLAYBOOK.md row whose pattern the reading names, or step 2, a retry at the next tick; the third failing tick in
/// a row (45 minutes, past Gmail's 30-minute backoff) opens step 4, the heal item. Step 3, the breaker, stays with the
/// runs it stops (check_empty_runs.py, canary.py, the 6 AM program's hold read): a hold on a Function process stops
/// nothing. A failure whose fix is outside self-heal is refused and named, never built; so is a heal lane's own.
/// </summary>
public static partial class HealLadder
{
    /// <summary>The standing words every heal item carries; dispatch_heal.is_heal checks them character for character.</summary>
    public const string Approved = "Steve 12:50 CST 11/19 (standing, heals of scheduled tasks)";

    /// <summary>The code heals a Central day may open (answer 10).</summary>
    public const int CodeHealsADay = 2;

    /// <summary>The heals one failure may have before it goes to NOT-WORKING.md.</summary>
    public const int Attempts = 2;

    /// <summary>The failing ticks in a row before a heal opens.</summary>
    public const int TicksBeforeHeal = 3;

    private const string Fixed = "# HEAL (fixed text, written by code; a model never writes or edits this item)";
    private const string RulesLine = "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The ladder for one tick in mode on.</summary>
    /// <param name="results">The tick's readings; only a fail climbs the ladder, a "not read" never does.</param>
    /// <param name="changeLog">The day's change log with this tick's lines, which date when each test began failing.</param>
    /// <param name="healed">HEALED.md as it stands.</param>
    /// <param name="playbook">PLAYBOOK.md.</param>
    /// <param name="open">The heal item names already in ready/, heal-shadow/ or running/.</param>
    /// <param name="live">Whether policy.json's heal_mode is on.</param>
    /// <param name="now">The tick's time.</param>
    public static HealPlan Plan(IReadOnlyList<WatchResult> results, string changeLog, string healed, string playbook, IReadOnlySet<string> open, bool live, DateTimeOffset now)
    {
        DateTime local = CentralClock.Local(now);
        string day = $"- {local.ToString("yyyy-MM-dd", Inv)} ";
        var before = (healed ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var lines = new List<string>();
        var notWorking = new List<string>();
        var items = new List<HealItem>();
        int code = before.Count(l => l.StartsWith(day, StringComparison.Ordinal) && l.Contains("| 4 heal item (code)", StringComparison.Ordinal));
        foreach (WatchResult r in results.Where(r => r.Pass == false))
        {
            string task = $"{r.Test} {r.Name}", sig = $"watch:{Key(task)}", reason = $"{r.Number} ({r.Source})";
            string since = Since(changeLog, r, local).ToString("M/d HH:mm", Inv);
            bool Today(string step) => before.Concat(lines).Any(l => l.StartsWith(day, StringComparison.Ordinal) && l.Contains($"| {step}", StringComparison.Ordinal) && l.EndsWith($"sig={sig}", StringComparison.Ordinal));
            if (HealWaits.Waiting(before, sig, local))
            {
                continue;
            }
            if (Outside(r) is { } letter)
            {
                if (!Today("4 refused"))
                {
                    lines.Add(Line(local, task, reason, "4 refused", $"outside self-heal ({letter}); sig={sig}"));
                    notWorking.Add(NotWorking(task, since, $"nothing: outside self-heal ({letter})", letter == "d" ? $"renew or sign in again: {r.Name}" : "nothing"));
                }
                continue;
            }
            if (!Today("1 playbook") && !Today("2 retry"))
            {
                lines.Add(Playbook(playbook, r) is { } row
                    ? Line(local, task, reason, "1 playbook", $"{row}; sig={sig}")
                    : Line(local, task, reason, "2 retry", $"the next tick, 15 minutes on; sig={sig}"));
            }
            string name = Name(task, local);
            if (Ticks(changeLog, r, local) < TicksBeforeHeal || open.Contains(name))
            {
                continue;
            }
            int tries = before.Skip(HealWaits.LandedAt(before, sig) + 1).Concat(lines).Count(l => l.EndsWith($"sig={sig}", StringComparison.Ordinal) && (l.Contains("| 4 heal item", StringComparison.Ordinal) || l.Contains("| 4 refused", StringComparison.Ordinal)));
            string kind = r.Test is "W5" or "W6" or "W11" or "W15" ? "data" : "code";
            string? refused = Key(r.Name).StartsWith("heal-", StringComparison.Ordinal) ? "a heal lane's own failure is never healed" : HealWaits.Product(r) ? HealWaits.ProductReading
                : kind == "code" && code >= CodeHealsADay && !HealWaits.RedMain(r) ? $"past the budget of {code} code heals today" : null;
            if (tries >= Attempts && refused is null)
            {
                notWorking.Add(NotWorking(task, since, $"{tries} heals ({sig})", "nothing"));
                continue;
            }
            if (refused is not null)
            {
                if (!Today("4 refused"))
                {
                    lines.Add(Line(local, task, reason, "4 refused", $"{refused}; sig={sig}"));
                    notWorking.Add(NotWorking(task, since, refused, "nothing"));
                }
                continue;
            }
            code += kind == "code" && !HealWaits.RedMain(r) ? 1 : 0;
            var item = new HealItem(live ? "dispatch/ready" : "dispatch/heal-shadow", name, string.Empty,
                string.Create(Inv, $"Watch reading {local:yyyy-MM-dd HH:mm} Central, failing {Ticks(changeLog, r, local)} ticks in a row since {since}:\n| {r.Test} | {r.Name} | {r.Word} | {r.Number} | {r.Source} |\n"));
            items.Add(item with { Text = Text(task, now, sig, kind, reason, $"J:\\{item.Fixture.Replace('/', '\\')}", r.Test is "W9" or "W11" ? "theyard" : "jobhunt") });
            lines.Add(Line(local, task, reason, $"4 heal item ({kind}{(HealWaits.RedMain(r) ? ", red main" : string.Empty)})", $"{(live ? "ready" : "heal-shadow (heal_mode shadow)")}\\{name}; sig={sig}"));
        }
        lines.InsertRange(0, HealWaits.Steps(results, before, local));
        return new HealPlan(lines, notWorking, items);
    }

    /// <summary>A task as a file name: "W12 Merger" is w12-merger, as dispatch_heal.key makes it.</summary>
    /// <param name="task">The task.</param>
    public static string Key(string task) => Slug().Replace((task ?? string.Empty).ToLowerInvariant(), "-").Trim('-') is { Length: > 0 } k ? k : "lane";

    /// <summary>The heal item's name for a task on a Central day.</summary>
    /// <param name="task">The task.</param>
    /// <param name="local">The Central time.</param>
    public static string Name(string task, DateTime local) => $"30-heal-{Key(task)}-{local.ToString("yyyyMMdd", Inv)}.md";

    /// <summary>
    /// Which letter of the outside list a failure's only fix falls under, or null when the ladder may heal it: the pounce's
    /// mode and a switch are send switches (e), a secret is his red line (d), a no-email day's breach is a send (a).
    /// </summary>
    /// <param name="r">The failing reading.</param>
    public static string? Outside(WatchResult r) => r.Test switch
    {
        "W2" => "e",
        "W13" => r.Number.StartsWith("missing", StringComparison.Ordinal) || r.Number.StartsWith("expires", StringComparison.Ordinal) ? "d" : "e",
        "W16" => "a",
        _ => null,
    };

    /// <summary>The failing ticks in a row: from the change log's newest line for the test, when it reads FAIL.</summary>
    /// <param name="log">The day's change log.</param>
    /// <param name="r">The reading.</param>
    /// <param name="local">The Central time now.</param>
    public static int Ticks(string log, WatchResult r, DateTime local) => (int)((local - Since(log, r, local)).TotalMinutes / 15) + 1;

    /// <summary>
    /// The NOT-WORKING.md lines a tick clears (the plan's 7c, a line leaves when its test passes twice): a watch line
    /// whose test passes this tick and has read pass since a tick before this one.
    /// </summary>
    /// <param name="notWorking">NOT-WORKING.md.</param>
    /// <param name="results">The tick's readings.</param>
    /// <param name="changeLog">The day's change log with this tick's lines.</param>
    /// <param name="local">The Central time now.</param>
    public static IReadOnlyList<string> Cleared(string notWorking, IReadOnlyList<WatchResult> results, string changeLog, DateTime local)
    {
        var cleared = new List<string>();
        foreach (string line in (notWorking ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("NOT WORKING: W", StringComparison.Ordinal)))
        {
            int since = line.IndexOf(" since ", StringComparison.Ordinal);
            string task = since > 0 ? line["NOT WORKING: ".Length..since] : string.Empty;
            if (results.FirstOrDefault(r => $"{r.Test} {r.Name}" == task) is { Pass: true } passing
                && Last(changeLog, passing, local) is ("pass", { } at) && local - at >= TimeSpan.FromMinutes(15))
            {
                cleared.Add(line);
            }
        }
        return cleared;
    }

    /// <summary>TODAY.md with NOT WORKING lines it does not hold yet put under its title, the first lines he reads.</summary>
    /// <param name="today">TODAY.md.</param>
    /// <param name="lines">The new NOT WORKING lines.</param>
    public static string OnTheCard(string today, IReadOnlyList<string> lines)
    {
        var fresh = lines.Where(l => !(today ?? string.Empty).Contains(l, StringComparison.Ordinal)).ToList();
        int title = (today ?? string.Empty).IndexOf("\n# ", StringComparison.Ordinal);
        int end = title < 0 ? -1 : today!.IndexOf('\n', title + 1);
        return fresh.Count == 0 || end < 0 ? today ?? string.Empty : today![..end] + "\n\n" + string.Join("\n", fresh) + today[end..];
    }

    private static DateTime Since(string log, WatchResult r, DateTime local) => Last(log, r, local) is ("FAIL", { } at) ? at : local;

    /// <summary>The change log's newest word for a test and the time it was written today, or (null, null).</summary>
    private static (string? Word, DateTime? At) Last(string log, WatchResult r, DateTime local)
    {
        (string? Word, DateTime? At) last = (null, null);
        foreach (string line in (log ?? string.Empty).Split('\n'))
        {
            string[] cells = line.TrimEnd('\r').Split('\t');
            if (cells.Length >= 4 && cells[1] == r.Test && cells[2] == r.Name.Replace('\t', ' ').Replace('\n', ' '))
            {
                last = TimeOnly.TryParseExact(cells[0], "HH:mm", Inv, DateTimeStyles.None, out var t) && local.Date.Add(t.ToTimeSpan()) <= local
                    ? (cells[3], local.Date.Add(t.ToTimeSpan())) : (cells[3], null);
            }
        }
        return last;
    }

    private static string? Playbook(string playbook, WatchResult r)
    {
        string read = $"{r.Name} {r.Number}";
        foreach (string line in (playbook ?? string.Empty).Split('\n'))
        {
            string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length >= 5 && cells[1].Length > 3 && cells[1] != "Reason pattern" && !cells[1].StartsWith("---", StringComparison.Ordinal)
                && read.Contains(cells[1], StringComparison.OrdinalIgnoreCase))
            {
                return $"PLAYBOOK.md row \"{cells[1]}\": {cells[2]} ({cells[3]})";
            }
        }
        return null;
    }

    private static string Line(DateTime local, string task, string reason, string step, string result) =>
        $"- {local.ToString("yyyy-MM-dd HH:mm", Inv)} CST | {task} | {reason.Replace('|', '/')} | {step} | {result}";

    private static string NotWorking(string what, string since, string tried, string needs) => $"NOT WORKING: {what} since {since}. Tried: {tried}. Needs you: {needs}.";

    private static string Text(string task, DateTimeOffset now, string sig, string kind, string reason, string fixture, string scope)
    {
        DateTime local = CentralClock.Local(now);
        string lane = $"heal-{Key(task)}-{local.ToString("yyyyMMdd", Inv)}";
        string ship = kind == "code"
            ? $"Ship it as one version through the cop (CLAUDE.md, Shipping; RULE 80): one commit on the origin/main you just fetched, push ready/{lane}-<sha8>, tests red first and mocks only (RULE 79). Add the fix's row to J:\\dispatch\\PLAYBOOK.md in the same change, so the next time it fixes itself at step 1."
            : "This is a data or lane-file heal: change only data or the lane's own file, with a backup beside it (_backup_<name>_pre-<lane>.<ext>), read back. Add the fix's row to J:\\dispatch\\PLAYBOOK.md.";
        (string, string)[] header =
        [
            ("Lane", lane), ("Heal", "true"), ("Now", "true"), ("Kind", kind), ("Signature", sig), ("Heal_for", Key(task)), ("Scope", scope),
            ("Model", kind == "code" ? "claude-opus-5-5" : "claude-sonnet-5-5"), ("Effort", "high"), ("Holds", "none"), ("Needs", "jdrive"),
            ("Stall_minutes", "45"), ("Max_hours", "2"), ("Fixture", fixture), ("Entered_by", $"watch {now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv)}"),
            ("Approved_by", Approved), ("Done_note", $"J:\\dispatch\\done\\{lane}-DONE.md"),
        ];
        return string.Concat(header.Select(h => $"{h.Item1}: {h.Item2}\n")) + $"""

            {Fixed}

            {RulesLine}

            # Heal {task}: {reason}

            Approved by Steve's standing words for heals of scheduled tasks, 12:50 CST 11/19: "This should apply to all scheduled task
            and it should run without any questions from me". Nobody is asked anything: this lane fixes, or writes why it could not.

            ## What failed
            - Task: {task}
            - Signature: {sig}
            - The failing run, as the test fixture: {fixture}

            ## What ships
            1. Read the fixture and the DONE notes it names; take the failure to its cause.
            2. Write a test that reproduces the fixture and fails before the fix.
            3. Fix the cause. {ship}
            4. The DONE note says what was fixed, the test, the PLAYBOOK.md row and one line `Waiting on: <Ddd MM/DD HH:MM> CST`, the
               task's next real run; or, when the fix is outside self-heal, which letter below it falls under, and nothing is built.

            ## Outside self-heal (plan 7b; never built, logged in HEALED.md as outside self-heal, counted as one attempt):
            (a) a new or wider send: a new channel, recipient class or template, changed words under Steve's name, a raised ceiling or
                cap (the LinkedIn ceilings, RULE 25, RULE 50's 3 in 60 days, the campaign's cap and ramp, one email a company a month,
                the Hunter cap);
            (b) a change to one of his rule files: systemRules.md, targeting-criteria.md, FIT-NUMBERS.md, standard-answers.md,
                authorization.md, outreach-limits.md, BLACKLIST.md, and the ceilings in CONVENTIONS.md (the dispatcher puts back any
                of these this lane changes);
            (c) money: a purchase, a plan or tier, a new paid key or service, a new Azure resource or size;
            (d) his red line: a code, a sign-in, a password, a new account, a CAPTCHA, terms, a covenant, arbitration or a release,
                and any expired sign-in or token;
            (e) a send switch moved toward sending, except setting a wiped switch back to the value SWITCHES.md records;
            (f) a deletion: a heal moves and never deletes;
            (g) a gate made easier: a lowered ratchet, floor or ceiling, a deleted or skipped test; a heal branch that touches a path
                in data/heal-outside.json goes red in the gate.

            Done when: the test is green, the fix is live, and the task's next run does real work or gives a reason PLAYBOOK.md accepts.

            """;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Slug();
}
