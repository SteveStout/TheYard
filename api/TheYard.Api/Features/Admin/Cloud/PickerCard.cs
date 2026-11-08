// The nightly picker's card (ADR-125, the addendum on the picker's card): the 2 o'clock hour's pick and the 1 AM board
// scan before it. The dials are the night's leads that passed the gates and the boards that did not answer; the rows say
// whether each ran, what the picker read and why postings were blocked, and what it cost. Counts only: never a company,
// a title or a lead.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

// #region picker-card
/// <summary>The nightly picker's card.</summary>
public static class PickerCard
{
    /// <summary>How long after a night's run the next one is overdue: a day and the hour it may run late in.</summary>
    public static readonly TimeSpan Due = TimeSpan.FromHours(25);

    /// <summary>The leads dial: a share that passed is news, not a fault, so it never goes amber.</summary>
    public static readonly (double Amber, double Red) PassedLevels = (1000, 1001);

    /// <summary>The boards dial: amber when a tenth of the boards did not answer, red at a quarter.</summary>
    public static readonly (double Amber, double Red) SilentBoardLevels = (10, 25);

    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pick"] = "the Nightly function's \"jobs:\" line and its request in Application Insights, the 2 o'clock hour",
        ["supply"] = "the same line: the candidates file's supply counts",
        ["scan"] = "the BoardScan function's \"board scan:\" line and its request in Application Insights, the 1 AM hour",
        ["cost"] = CardFrame.TheBill,
        ["The hour"] = "the Function's CentralClock.IsJobsHour",
        ["Candidates file"] = "the Function's JobsFiles, on the share",
        ["The judgment"] = "JOBS-LANE.md on the share, the jobs lane",
        ["GB-seconds"] = "the Function's execution units, not read",
        ["Cost"] = CardFrame.TheBill,
        ["Live read"] = CardFrame.ThisProcess,
    };

    /// <summary>Draws the card.</summary>
    /// <param name="inputs">The kept minutes and hours.</param>
    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? leads = ResourceView.Now(inputs, "leads"), passed = ResourceView.Now(inputs, "passed");
        double? boards = ResourceView.Now(inputs, "boards"), answered = ResourceView.Now(inputs, "answered");
        double? silent = boards is > 0 && answered is { } a ? Math.Round(100 * (boards.Value - a) / boards.Value, 1) : null;
        var dials = new[]
        {
            new CloudDial("Leads that passed the gates", ResourceView.Share(passed, leads), ResourceView.Number(passed), leads is null ? "not read" : $"of {ResourceView.Number(leads)} alert leads, the newest night", PassedLevels.Amber, PassedLevels.Red, null, null, passed is null ? "info" : "good"),
            new CloudDial("Boards that did not answer", silent, silent is null ? "not read" : $"{ResourceView.Number(boards - answered)}", boards is null ? "not read" : $"of {ResourceView.Number(boards)} boards, the newest scan", SilentBoardLevels.Amber, SilentBoardLevels.Red, null, null,
                silent is null ? "info" : silent >= SilentBoardLevels.Red ? "alarm" : silent >= SilentBoardLevels.Amber ? "warning" : "good"),
        };
        var rows = new List<CloudRow> { Run(inputs, "pick", "Did the 2 AM pick run?", "pick"), Supply(inputs), Run(inputs, "scan", "Did the 1 AM board scan run?", "board scan"), CostRow(inputs) };
        var readout = new CloudReadout(
            "The nightly picker, now",
            ResourceView.Uptime(inputs, 60),
            $"nights read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("The hour", "the 2 o'clock hour Central, on the Nightly function, after the 1 AM board scan", "info"),
                new("Candidates file", "nightly/jobs-candidates-<date>.tsv on the job-hunt share, its supply line first", "info"),
                new("The judgment", "the jobs lane ranks what passed: READY, NEEDS STEVE, UNCONFIRMED", "info"),
                new("GB-seconds", "not read: the firing's execution units are not split out of the Function's", "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            "Counts only: the card never shows a company, a title or a lead.");
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0),
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault(PickerSource.NoteFact) ?? ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "hour")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The nightly picker",
            "Azure Functions, the 1 AM board scan and the 2 o'clock pick, once a night",
            null,
            dials,
            [ResourceView.Spark(inputs, "passed", "Passed, the kept hours", leads), ResourceView.Spark(inputs, "answered", "Boards answered, the kept hours", boards)],
            ResourceView.Chart(inputs, "Leads and passed, each read", ("leads", "leads", null, true), ("passed", "passed", null, true), 0, "leads teal · passed gold · the newest night's counts, read an hour apart"),
            rows,
            readout);
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>Did a night's run happen: when, how long, what it said; red when it failed, amber past a day and an hour.</summary>
    public static CloudRow Run(ResourceCardInputs inputs, string kind, string question, string what)
    {
        var facts = ResourceView.Latest(inputs)?.Facts;
        if (facts?.GetValueOrDefault($"{kind}_at") is not { } atText || !DateTimeOffset.TryParse(atText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return new CloudRow(kind, question, facts is null ? "not read yet" : $"no {what} in the last two days", [new("the newest line in the workspace", null)], facts is null ? "info" : "warning");
        }
        bool failed = facts.GetValueOrDefault($"{kind}_ok") == "False" || (kind == "pick" && facts.GetValueOrDefault("pick_failed") == "yes");
        string took = PickerSource.Number(facts.GetValueOrDefault($"{kind}_ms")) is { } ms ? $"{(ms / 1000).ToString("0.#", CultureInfo.InvariantCulture)} s" : "time not read";
        string when = NightlySlots.Local(at).ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
        string tone = failed ? "alarm" : inputs.Now - at > Due ? "warning" : "good";
        string answer = failed ? $"failed, {when}" : inputs.Now - at > Due ? $"late: the newest is {when}" : $"yes, {when}, {took}";
        string detail = kind == "pick" ? $"dispatch: {facts.GetValueOrDefault("dispatch") ?? "not read"}" : $"{facts.GetValueOrDefault("answered") ?? "?"} of {facts.GetValueOrDefault("boards") ?? "?"} boards answered, {facts.GetValueOrDefault("postings") ?? "?"} postings";
        return new CloudRow(kind, question, answer, [new(detail, null)], tone);
    }

    /// <summary>What the picker read: the leads, what passed, what the feeds confirmed, and why postings were blocked.</summary>
    public static CloudRow Supply(ResourceCardInputs inputs)
    {
        var facts = ResourceView.Latest(inputs)?.Facts;
        if (facts?.GetValueOrDefault("leads") is not { Length: > 0 } leads)
        {
            return new CloudRow("supply", "What did it read?", "not read yet", [new("the supply line", null)], "info");
        }
        string? blocked = facts.GetValueOrDefault("blocked");
        string answer = $"{leads} alert leads, {facts.GetValueOrDefault("passed")} passed, {facts.GetValueOrDefault("confirmed")} confirmed by the feeds, {facts.GetValueOrDefault("failed")} failed";
        return string.IsNullOrEmpty(blocked)
            ? new CloudRow("supply", "What did it read?", answer, [new("the boards' own postings and the alert leads", null)], "good")
            : new CloudRow("supply", "What did it read?", answer, [new($"postings blocked: {blocked}, so the night rests on the alert leads alone; the postings tables are published by a laptop file (NIGHTJOBS-DONE handoff item 1)", null)], "warning");
    }

    /// <summary>What it costs: the Function's line of the bill, which the scan and the pick share with the rest.</summary>
    public static CloudRow CostRow(ResourceCardInputs inputs) =>
        new("cost", "What does it cost?", inputs.CostMonth is { } dollars ? $"${dollars.ToString("0.00", CultureInfo.InvariantCulture)} this month, the whole Function" : "not on the bill yet",
            [new("the scan and the pick run inside the Function's own firings; their share is not split out", null)], "info");
}
// #endregion picker-card
