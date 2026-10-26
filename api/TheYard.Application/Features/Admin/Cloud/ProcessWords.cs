// The words the nightly card's six process sections and their run histories print (ADR-109, the addendum on
// the six processes): what a run came to, what it read and wrote as one sentence built from its counts by one
// table a process, how long it took, the Central day and time, the window's totals and the thirty-day line.
// Pure, so every word is held by a test without a host or a workspace.
using System.Globalization;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region process-words
/// <summary>The process sections' words.</summary>
public static class ProcessWords
{
    /// <summary>The words a run line's outcome can be, as the Function's RunLine writes them.</summary>
    public static readonly IReadOnlyList<string> Outcomes = ["success", "late", "blocked", "failed", "planned", "unchanged"];

    /// <summary>The one-word reasons the Function writes, in words.</summary>
    private static readonly IReadOnlyDictionary<string, string> Reasons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["google_token"] = "the Google token was refused",
        ["google_settings"] = "the Google settings are not there",
        ["mismatch"] = "the Sheet and the tracker disagree",
        ["switch_off"] = "the switch is off",
        ["not_a_slot"] = "not a slot hour",
        ["part_failed"] = "a part of the pass failed",
        ["not_the_hour"] = "not the 8 o'clock hour",
        ["no_database"] = "the database is not set",
    };

    /// <summary>The window in words: "24 hours", "7 days", "30 days".</summary>
    /// <param name="window">24h, 7d or 30d.</param>
    public static string WindowWords(string window) => window switch
    {
        "24h" => "24 hours",
        "30d" => "30 days",
        _ => "7 days",
    };

    /// <summary>The run's tone: good for success, warning for late, alarm for blocked or failed, info for planned, quiet for unchanged.</summary>
    /// <param name="outcome">The outcome word.</param>
    public static string Tone(string outcome) => outcome switch
    {
        "success" => "good",
        "late" => "warning",
        "blocked" or "failed" => "alarm",
        "planned" => "info",
        _ => "quiet",
    };

    /// <summary>What a run came to, in words: "success", "late, 1 min 2 s after its slot", "blocked, the Google token was refused".</summary>
    /// <param name="run">The run.</param>
    public static string Came(ProcessRun run) => run.Outcome switch
    {
        "late" when long.TryParse(run.Detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds) => $"late, {Took(seconds * 1000)} after its slot",
        "blocked" => $"blocked, {Reason(run.Detail, "no reason given")}",
        "failed" => run.Detail is null ? "failed" : $"failed, {Reason(run.Detail, run.Detail)}",
        "planned" => "planned, acted on nothing",
        "unchanged" when run.Detail is not null => $"unchanged, {Reason(run.Detail, run.Detail)}",
        _ => run.Outcome,
    };

    /// <summary>What a run read and wrote, counts only, by its process's own table.</summary>
    /// <param name="process">The process's key.</param>
    /// <param name="run">The run.</param>
    public static string Sentence(string process, ProcessRun run)
    {
        if (!run.HasLine)
        {
            return run.Outcome == "failed" ? "the request failed; no run line, from before the processes wrote one" : "no run line, from before the processes wrote one: the request row only";
        }
        if (run.Detail is "switch_off" or "not_a_slot" or "not_the_hour" or "no_database" or "google_token" or "google_settings")
        {
            return Reason(run.Detail, run.Detail) + (run.Detail == "google_token" ? "; the pass skipped its later parts" : "");
        }
        long? Count(string name) => run.Counts.TryGetValue(name, out long value) ? value : null;
        var parts = new List<string>();
        void Add(string name, Func<long, string> words)
        {
            if (Count(name) is { } value)
            {
                parts.Add(words(value));
            }
        }
        switch (process)
        {
            case "AtsSweep":
                Add("read", n => $"{n} read");
                Add("archived", n => $"{n} archived");
                Add("leads", n => Count("leads_passed") is { } passed ? $"{Plural(n, "lead")}, {passed} passed" : Plural(n, "lead"));
                Add("unjudged", n => $"{n} to triage");
                Add("waiting", n => $"{n} waiting");
                break;
            case "Merger":
                Add("merged", n => n == 0 ? "nothing waiting" : $"{n} merged");
                // The tracker's SQL copy, written after the merge: the rows it copied, the day's parity check, a failed copy.
                Add("tracker_sql_written", n => n == 0 ? "the SQL copy in step" : $"{Plural(n, "row")} copied");
                Add("tracker_sql_parity", n => n == 1 ? "parity same" : "PARITY DIFFERENT");
                Add("tracker_sql_failed", _ => "copy failed, retried next pass");
                break;
            case "Nightly":
                Add("dispatched", n => n == 1 ? $"dispatched the {Slot(run.Detail)}" : $"the {Slot(run.Detail)} dispatch was refused");
                break;
            case "SheetMirror":
                Add("would_append", n => run.Outcome == "success" ? $"{Plural(n, "row")} appended" : $"{Plural(n, "row")} would append");
                Add("sheet_rows", n => $"Sheet {N(n)}");
                Add("master_rows", n => $"tracker {N(n)}");
                if (run.Detail == "mismatch")
                {
                    parts.Insert(0, "stopped where the Sheet and the tracker disagree");
                }
                break;
            case "DailyCard":
                Add("written", n => n == 1 ? "the card written" : "unchanged, not written");
                break;
            case "CampaignSend":
                Add("open", n => $"{Plural(n, "lead")} open");
                Add("eligible", n => $"{n} eligible");
                Add("written", n => $"{n} written in shadow");
                Add("sent", n => $"{n} sent");
                Add("held", n => $"{n} held");
                break;
            case "MergerHealth":
                Add("waiting", n => $"{n} waiting");
                Add("past_5", n => $"{n} past 5 min");
                Add("past_10", n => $"{n} past 10");
                Add("newest_log_age_s", n => $"newest log {Took(n * 1000)}");
                break;
        }
        return parts.Count == 0 ? run.Outcome == "failed" ? "threw before it counted anything" : "nothing counted" : string.Join(" · ", parts);
    }

    /// <summary>How long a run took: "0.4 s", "42 s", "1 min 31 s", "2 h 4 min".</summary>
    /// <param name="ms">Milliseconds.</param>
    public static string Took(long ms)
    {
        if (ms < 10_000)
        {
            return (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";
        }
        long seconds = (long)Math.Round(ms / 1000.0);
        return seconds < 60 ? $"{seconds} s"
            : seconds < 3600 ? (seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60} min {seconds % 60} s")
            : $"{seconds / 3600} h {seconds % 3600 / 60} min";
    }

    /// <summary>The Central day as the history's day row prints it: "Sat 24 Oct".</summary>
    /// <param name="at">The moment, UTC.</param>
    public static string Day(DateTimeOffset at) => NightlySlots.Local(at).ToString("ddd d MMM", CultureInfo.InvariantCulture);

    /// <summary>The Central time: "11:20".</summary>
    /// <param name="at">The moment, UTC.</param>
    public static string Clock(DateTimeOffset at) => NightlySlots.Local(at).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The history's summary count for a window, by what the process's history lists.</summary>
    /// <param name="process">The process.</param>
    /// <param name="tally">The window's totals.</param>
    /// <param name="words">The window in words.</param>
    public static string CountLine(FunctionProcess process, ProcessTally tally, string words) => process.Key switch
    {
        "Merger" => $"{N(tally.Runs)} passes in {words}; the {N(tally.Shown)} that merged something or moved the SQL copy are listed, the rest did neither",
        "Nightly" => $"{N(tally.Shown)} slots in {words}, every one listed; the hours between are counted, not listed",
        "DailyCard" => $"{N(tally.Runs)} runs in {words}; the {N(tally.Shown)} that wrote the card are listed",
        "MergerHealth" => $"{N(tally.Runs)} answers in {words}, the slowest {Math.Min(tally.Shown, 20)} first",
        _ => $"{N(tally.Runs)} runs in {words}, the newest {Math.Min(tally.Shown, 20)} first",
    };

    /// <summary>The foot's totals: "188 in 7 days · 2 late · 2 blocked · 0 failed".</summary>
    /// <param name="tally">The window's totals.</param>
    /// <param name="words">The window in words.</param>
    public static string TotalsLine(ProcessTally tally, string words) =>
        $"{N(tally.Runs)} in {words} · {N(tally.Late)} late · {N(tally.Blocked)} blocked · {N(tally.Failed)} failed";

    /// <summary>The section's thirty-day line, by the process's own measure.</summary>
    /// <param name="process">The process.</param>
    /// <param name="tally">Thirty days' totals.</param>
    public static string ThirtyDays(FunctionProcess process, ProcessTally tally)
    {
        long failed = tally.Failed + tally.Blocked;
        return process.Key switch
        {
            "Merger" => $"{N(tally.Runs)} passes · {N(tally.Acted)} merges · {N(failed)} failed",
            "Nightly" => $"{N(tally.Acted)} slots dispatched · {N(tally.Late)} late · {N(failed)} failed",
            "SheetMirror" => $"{N(tally.Planned)} plans · {N(tally.Acted)} appends · {N(failed)} failed or blocked",
            "DailyCard" => $"{N(tally.Runs)} runs · {N(tally.Acted)} writes · {N(failed)} failed",
            "MergerHealth" => $"{N(tally.Runs)} answers · {N(failed)} failed" + (tally.P95Ms is { } p95 ? $" · p95 {Took((long)p95)} on its slowest day" : ""),
            _ => $"{N(Math.Max(0, tally.Runs - tally.Late - failed))} of {N(tally.Runs)} on time · {N(tally.Late)} late · {N(failed)} failed or blocked",
        };
    }

    /// <summary>A day's cell on the strip, by its worst run: fail, late, shadow (planned and nothing acted), ok, or skip where no run was.</summary>
    /// <param name="tally">The day's totals; null for a day with none.</param>
    public static string Cell(ProcessTally? tally) => tally switch
    {
        null or { Runs: 0 } => "skip",
        { Failed: > 0 } or { Blocked: > 0 } => "fail",
        { Late: > 0 } => "late",
        { Planned: > 0, Acted: 0 } => "shadow",
        _ => "ok",
    };

    /// <summary>
    /// Whether the tracker's SQL copy needs a look: the newest parity check found the copy different from the CSV,
    /// or the newest pass failed its copy. Either turns the Merger's section amber.
    /// </summary>
    /// <param name="newest">The Merger's newest pass.</param>
    /// <param name="newestParity">The Merger's newest pass that checked parity.</param>
    public static bool CopyNeedsALook(ProcessRun? newest, ProcessRun? newestParity) =>
        newestParity?.Counts.GetValueOrDefault("tracker_sql_parity", 1) == 0 || newest?.Counts.ContainsKey("tracker_sql_failed") == true;

    /// <summary>A number with thousands separators.</summary>
    public static string N(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static string Plural(long n, string word) => n == 1 ? $"1 {word}" : $"{N(n)} {word}s";

    private static string Reason(string? detail, string fallback) => detail is not null && Reasons.TryGetValue(detail, out string? words) ? words : fallback.Replace('_', ' ');

    private static string Slot(string? detail) => detail switch
    {
        // A late dispatch's detail is its seconds late, not its slot's name.
        null => "slot",
        _ when detail.All(char.IsAsciiDigit) => "slot",
        "nightly" => "4 AM reply run",
        "bookkeeping" => "records run",
        _ => detail.Replace('_', ' '),
    };
}
// #endregion process-words
