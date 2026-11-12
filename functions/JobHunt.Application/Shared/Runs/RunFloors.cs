namespace JobHunt.Application.Shared.Runs;

/// <summary>One process's floor: the count that proves a run did its work, the least it must reach, and over how many tries.</summary>
/// <param name="Process">The run line's process name.</param>
/// <param name="Proves">The run line's count that proves the work, or null when empty is normal.</param>
/// <param name="Floor">The least that count must reach.</param>
/// <param name="Tries">How many tries in a row may come under it before the run is blocked; 1 is judged by the run itself.</param>
/// <param name="Normal">Why an empty run is normal for this process, when it is.</param>
public sealed record RunFloor(string Process, string? Proves, int Floor, int Tries, string? Normal);

/// <summary>
/// An empty run counts as failed. Every process that writes a run line has a row: the
/// count that proves it worked and its floor, or the words for why empty is normal for it. A run that comes to success
/// under its floor comes to blocked with detail zero_results instead, so the run line, and the alert that reads it,
/// say what the run did. No seventh outcome word: RunLine.Outcomes stays six. A floor over more than one try is kept
/// by the part that counts the tries (the finder's ten asked with none found, AddressSearch); this table names it.
/// </summary>
public static class RunFloors
{
    /// <summary>Every process's row.</summary>
    public static readonly IReadOnlyList<RunFloor> All =
    [
        new("AtsSweep", null, 0, 1, "an inbox with no new mail in half an hour is ordinary; a part that fails fails the run"),
        new("AtsSweep finder", "finder_found", 1, 10, null),
        new("Nightly", "dispatched", 1, 1, null),
        new("FreshScan", "read", 1, 1, null),
        new("BoardScan", null, 0, 1, "it writes blocked no_postings itself when a scan reads nothing"),
        new("Merger", null, 0, 1, "nothing waiting in the spool is the usual minute"),
        new("MergerHealth", null, 0, 1, "it answers on request and looks nothing up"),
        new("SheetMirror", null, 0, 1, "no new tracker row since the last mirror is the usual quarter hour"),
        new("DailyCard", null, 0, 1, "it writes once a day; every other hour is not its hour"),
        new("TodayCard", null, 0, 1, "it writes once a day; every other hour is not its hour"),
        new("TodayCardHandRun", null, 0, 1, "a hand run writes one card or says why not"),
        new("Funnel", null, 0, 1, "it writes the day before's counts whatever the day held; a day with no scan, touch or reply is a day of zeros, and the card names it"),
        new("CampaignSend", null, 0, 1, "shadow and the holds send nothing by design; a silent 8 AM has its own alert"),
        new("CampaignTestSend", null, 0, 1, "it sends one test or names what it lacks"),
        new("NetworkLogPass", null, 0, 1, "a day with no two-way thread happens; a failed read fails the run and a silent 6 AM has its own alert"),
        new("Pounce", null, 0, 1, "a quarter hour with no fresh role is ordinary"),
        new("PounceDay", null, 0, 1, "the day's line is written whatever the day held"),
        new("PounceFollowUp", null, 0, 1, "a quarter hour with no code due is ordinary"),
        new("ReplyDraft", null, 0, 1, "five minutes with no new human mail is ordinary"),
    ];

    /// <summary>The row for a process, or null.</summary>
    public static RunFloor? For(string process) => All.FirstOrDefault(f => f.Process == process);

    /// <summary>The run, blocked with detail zero_results when it came to success under a floor it judges itself.</summary>
    public static RunLine Apply(RunLine run) =>
        run.Outcome == "success" && For(run.Process) is { Proves: { } count, Tries: 1 } floor && run.Counts.GetValueOrDefault(count) < floor.Floor
            ? run.Came("blocked", "zero_results")
            : run;
}
