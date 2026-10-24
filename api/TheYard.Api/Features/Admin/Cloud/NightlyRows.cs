// The nightly card's questions (ADR: The Nightly Function on the Admin tab): is it firing, is the
// tracker merger keeping up (ADR: The tracker merger runs in Azure), what the runs came to, what it
// costs line by line, and what it is allowed to do. Its own file so the card's file holds the dials,
// the chart and the readout, and each stays short.
using System.Globalization;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The nightly card's question rows, and the words its run log is printed in.</summary>
public static class NightlyRows
{
    /// <summary>What GitHub calls a run that ended badly; a cancelled or skipped run is neither.</summary>
    public static readonly IReadOnlyList<string> Failed = ["failure", "timed_out", "startup_failure"];

    /// <summary>The run log's columns.</summary>
    public static readonly IReadOnlyList<string> Columns = ["When (Central)", "Slot", "Started by", "Run", "Took", "Came to"];

    /// <summary>The sentence a row prints in place of an answer it needs Azure for while the identity holds no role on the group.</summary>
    /// <param name="group">The Function's resource group, from configuration.</param>
    public static string NoRole(string group) => $"not read: no role on {group}";

    /// <summary>Why a line that needs the hour has no answer: no hour kept yet, or what the hour itself said in place of the record.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static string NoHour(ResourceCardInputs inputs) =>
        ResourceView.Latest(inputs) is not { } hour ? "not read: no hour kept yet"
            : "not read: " + (hour.Facts.GetValueOrDefault("site") ?? hour.Facts.GetValueOrDefault("azure") ?? "the hour named nothing");

    /// <summary>Why a line that needs the kept minutes has no answer: the keep's own reason, no hour yet, or no minute yet.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static string NoMinutes(ResourceCardInputs inputs) =>
        !inputs.Availability.Available ? "not read: " + ResourceView.KeepReason(inputs)
            : inputs.Hours.Count == 0 ? "not read: no hour kept yet"
            : "not read: no minute kept yet";

    /// <summary>Why the run log is empty: GitHub's answer as the hour wrote it, or that no hour has asked GitHub yet.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static string NoRuns(ResourceCardInputs inputs) =>
        ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("github") is not { } read ? "no hour kept yet"
            : read.StartsWith("not read: ", StringComparison.Ordinal) ? read["not read: ".Length..]
            : "GitHub listed no runs in 30 days";

    /// <summary>The run log's tone: warning when the newest run failed, alarm when the two newest did, good otherwise, information with no runs.</summary>
    /// <param name="runs">The runs, newest first.</param>
    public static string RunsTone(IReadOnlyList<NightlyRun> runs) =>
        runs.Count == 0 ? "info"
            : runs.Count >= 2 && Failed.Contains(runs[0].CameTo) && Failed.Contains(runs[1].CameTo) ? "alarm"
            : Failed.Contains(runs[0].CameTo) ? "warning"
            : "good";

    /// <summary>How long a run took, in the readout's words.</summary>
    /// <param name="seconds">Its seconds; null while it runs.</param>
    public static string Took(long? seconds) =>
        seconds is not { } s ? "still running"
            : s < 60 ? $"{s} s"
            : $"{s / 60} min {s % 60} s";

    /// <summary>What a run came to, with the dry run said beside it.</summary>
    /// <param name="run">The run.</param>
    public static string CameTo(NightlyRun run) => run.Dry ? run.CameTo + GitHubRuns.DrySuffix : run.CameTo;

    /// <summary>The runs as the row's table, newest first.</summary>
    /// <param name="runs">The runs, newest first.</param>
    public static CloudTable RunTable(IReadOnlyList<NightlyRun> runs) =>
        runs.Count == 0 ? CloudTable.None : new CloudTable(Columns, [.. runs.Select(run => (IReadOnlyList<CloudLink>)
        [
            new(NightlySlots.Local(run.At).ToString("MMM d", CultureInfo.InvariantCulture), null),
            new(run.Slot, null),
            new(run.StartedBy, null),
            new(run.Lane, null),
            new(Took(run.TookSeconds), null),
            new(CameTo(run), null),
        ])]);

    /// <summary>A month to date as the card prints it.</summary>
    /// <param name="dollars">The bill's figure; null when the bill does not name it yet.</param>
    public static string Dollars(double? dollars) => dollars is { } d ? "$" + d.ToString("0.00", CultureInfo.InvariantCulture) : "not on the bill yet";

    /// <summary>The month to date over every line the bill names; null when it names none.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static double? CostTotal(ResourceCardInputs inputs)
    {
        var known = (inputs.CostLines ?? new Dictionary<string, double?>()).Values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : Math.Round(known.Sum(), 2);
    }

    /// <summary>The words the merger row says when no kept minute in the last hour carries its reading.</summary>
    public const string MergerUnread = "not read: no merger reading kept in the last hour";

    /// <summary>
    /// Is the tracker merger keeping up: the newest kept minute's five numbers from the Function's
    /// health route (MergerReading). Good when nothing has waited past five minutes; information while
    /// a file is past five; a warning once any file has waited past ten (CloudThresholds.MergerWait).
    /// </summary>
    /// <param name="inputs">The card's inputs.</param>
    public static CloudRow MergerRow(ResourceCardInputs inputs)
    {
        const string question = "Is the tracker merger keeping up?";
        string source = "the Function's health route, counts and ages only";
        if (MergerReading.Newest(inputs.LastHour) is not { } minute)
        {
            return new CloudRow("merger", question, MergerUnread,
                [new("read once a minute from the Function, which lists its two spool folders and reads no file", null)], "info")
            { Source = source };
        }
        var values = minute.Values;
        int waiting = MergerReading.Count(values.GetValueOrDefault(MergerReading.Waiting));
        int over5 = MergerReading.Count(values.GetValueOrDefault(MergerReading.Over5));
        int over10 = MergerReading.Count(values.GetValueOrDefault(MergerReading.Over10));
        string tone = over10 > 0 ? "warning" : over5 > 0 ? "info" : "good";
        string value = over10 > 0 ? $"{MergerReading.Files(over10)} waiting over {CloudThresholds.MergerWait.Red} minutes"
            : over5 > 0 ? $"{MergerReading.Files(over5)} waiting over {CloudThresholds.MergerWait.Amber} minutes"
            : "keeping up";
        string log = values.GetValueOrDefault(MergerReading.LogAge) is { } age ? $"newest merge.log line {MergerReading.Age(age)} ago" : "no merge.log on the share";
        string oldest = values.GetValueOrDefault(MergerReading.Oldest) is { } wait ? $", the oldest {MergerReading.Age(wait)}" : "";
        return new CloudRow("merger", question, value,
            [
                new(log, null),
                new($"{MergerReading.Files(waiting)} waiting in tracker-inbox/ and inbox/tracker-inbox/{oldest}; {over5} past {CloudThresholds.MergerWait.Amber} minutes, {over10} past {CloudThresholds.MergerWait.Red}", null),
                new($"read {NightlySlots.Local(minute.At).ToString("HH:mm", CultureInfo.InvariantCulture)} Central, from the Function, which lists the two folders and reads no file", null),
            ],
            tone)
        { Source = source };
    }

    /// <summary>The card's rows.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="names">The Function's names, from configuration.</param>
    /// <param name="tally">The thirty days' slots, judged.</param>
    /// <param name="missTone">The firing dial's tone, read on the misses.</param>
    /// <param name="runs">The run log, newest first.</param>
    /// <param name="roleMissing">True when the identity holds no role on the Function's group.</param>
    public static IReadOnlyList<CloudRow> Rows(ResourceCardInputs inputs, NightlyResources names, SlotTally tally, PlanTone missTone, IReadOnlyList<NightlyRun> runs, bool roleMissing)
    {
        string noRole = NoRole(names.Group);
        var lines = inputs.CostLines ?? new Dictionary<string, double?>();
        return
        [
            new("firing", "Is it firing?",
                roleMissing ? noRole : tally.Due == 0 ? "no slot kept yet" : $"{tally.OnTime} of {tally.Due} slots on time",
                [new(roleMissing ? noRole : $"{tally.OnTime} fired in their first five minutes · {tally.Late} late · {tally.Missed} missed, over the days kept", null),
                    new("slots: 4:00 AM Central for the reply run; midnight, 8 AM, noon, 4 PM and 8 PM for the records run", null)],
                roleMissing ? "info" : CloudTones.Of(missTone))
            { Source = "Azure Monitor, " + NightlyMetrics.Executions },
            MergerRow(inputs),
            new("runs", "Run log",
                runs.Count == 0 ? $"no runs read yet: {NoRuns(inputs)}" : $"{runs.Count} runs in 30 days, the newest {CameTo(runs[0])}",
                [new($"each run kept a year under its GitHub id · the workflow {names.Workflow} in {names.Repo}", $"https://github.com/{names.Repo}/actions/workflows/{names.Workflow}")],
                RunsTone(runs))
            { Table = RunTable(runs), Source = $"GitHub Actions, {names.Workflow} runs" },
            new("cost", "Is it costing anything?", Dollars(CostTotal(inputs)) + (CostTotal(inputs) is null ? "" : " this month"),
                [.. names.CostNames.Select(name => new CloudLink($"{name}: {Dollars(lines.GetValueOrDefault(name))}", null)),
                    new("share snapshots kept: " + NightlyCard.SnapshotWords(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault(NightlySource.SnapshotFact), roleMissing ? noRole : NoHour(inputs)) + "; read from Resource Manager, the account key never goes in the site's settings", null)],
                "info")
            { Source = CardFrame.TheBill },
            new("allowed", "What is it allowed to do?", "one role on one file share, and one kind of dispatch",
                [new($"its identity holds Storage File Data SMB Share Contributor on the one share in {names.Storage}, and nothing else, which the role check script beside the Function proves", null),
                    new($"its token may send repository_dispatch to {names.Repo}, nothing more", null),
                    new($"this site's identity reads {names.Group} with Reader and Monitoring Reader and writes nothing there, which the same script proves", null),
                    new("No file in the share is opened here: the card sees the clock, the metrics, the share's snapshot list from Resource Manager, the merger's counts and ages from the Function, the public workflow runs and the bill.", null)],
                "good")
            { Source = "the Function's template and role check, in this repository" },
        ];
    }
}
