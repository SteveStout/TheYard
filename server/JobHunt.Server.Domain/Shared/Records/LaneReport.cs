using System.Globalization;
using System.Text;

namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>Everything one lane's DONE note and card line are written from.</summary>
/// <param name="Lane">The lane.</param>
/// <param name="Model">The model id.</param>
/// <param name="Dry">True for a dry run.</param>
/// <param name="Started">When the lane began.</param>
/// <param name="Ended">When it ended.</param>
/// <param name="Before">The tracker's counts at the start, null when they could not be read.</param>
/// <param name="Rows">The worklist.</param>
/// <param name="Dropped">What the gates left out.</param>
/// <param name="Answer">The model's result, null when it gave none.</param>
/// <param name="Sends">The claimed sends, with what their evidence lacked.</param>
/// <param name="Lines">Lines the note carries whole: reads, holds, blockers with their fix.</param>
/// <param name="Held">Why the lane did not run, or null.</param>
/// <param name="CutShort">True when the model was killed, or ended with no result line.</param>
public sealed record LaneReport(
    Lane Lane,
    string Model,
    bool Dry,
    DateTimeOffset Started,
    DateTimeOffset Ended,
    OutreachCounts? Before,
    IReadOnlyList<WorkRow> Rows,
    IReadOnlyList<Dropped> Dropped,
    LaneResult? Answer,
    IReadOnlyList<SendRecord> Sends,
    IReadOnlyList<string> Lines,
    string? Held,
    bool CutShort = false)
{
    /// <summary>Sends that passed their evidence.</summary>
    public int Verified => Sends.Count(send => send.Missing.Count == 0);

    /// <summary>True when every send stood and nothing broke: a run that counts toward a lane's cheaper model.</summary>
    public bool Clean => Held is null && !CutShort && Answer is not null && Sends.Count > 0 && Verified == Sends.Count && Answer.StopCondition == "none" && !NoRecount;

    /// <summary>
    /// True when the LinkedIn work of the day must end: a stop condition, a row the model called blocked, a send on a row the
    /// worklist never held, a browser that stopped answering, or rows it was given and no result. Failing closed is the point: a run that ended with no answer
    /// may have ended on a checkpoint.
    /// </summary>
    public bool ClosesLinkedIn =>
        CutShort || Answer is null ? Rows.Count > 0 : Answer.StopCondition != "none" || Answer.Rows.Any(row => row.Outcome == "blocked" || OffTheList(row)) || Answer.Rows.Count < Rows.Count;

    private bool OffTheList(RowResult row) => row.Outcome == "sent" && Rows.All(listed => listed.Row != row.Row);

    /// <summary>True when invites went out with no live count read first: the sent page governs, so that is a defect.</summary>
    public bool NoRecount => Lane == Lane.Invites && Sends.Count > 0 && Answer?.LiveInviteCount is null;
}

/// <summary>A lane's DONE.md, in the house shape: what was read, every row, every skip, the ceilings, blockers, the times.</summary>
public static class DoneNote
{
    /// <summary>The whole note.</summary>
    /// <param name="report">The lane's report.</param>
    public static string Render(LaneReport report)
    {
        var note = new StringBuilder();
        DateTime start = RunSlot.Local(report.Started), end = RunSlot.Local(report.Ended);
        note.AppendLine("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).");
        note.AppendLine();
        note.AppendLine(Invariant($"# DONE: {RegistryRows.Lane} {RunSlot.Word(report.Lane)}, {start:ddd M/d HH:mm} to {end:HH:mm} Central, {report.Model}{(report.Dry ? ", DRY RUN" : string.Empty)}"));
        note.AppendLine();
        if (report.Held is not null)
        {
            note.AppendLine("HELD: " + report.Held);
            note.AppendLine();
        }
        note.AppendLine("## Read and noted");
        foreach (string line in report.Lines)
        {
            note.AppendLine("- " + line);
        }
        if (report.Before is { } counts)
        {
            note.AppendLine(Invariant($"- Tracker counts at the start (the floor): {counts.InvitesWeek} invites in 7 days, {counts.InvitesToday} today, {counts.MessagesToday} messages today, {counts.ActionsToday} actions today."));
        }
        if (report.Answer?.LiveInviteCount is { } live)
        {
            note.AppendLine(Invariant($"- Live count on the sent page (governs): {live} under 7 days. After this run: {live + report.Sends.Count}."));
        }
        if (report.NoRecount)
        {
            note.AppendLine("- DEFECT: invites went out with no live count read first.");
        }
        note.AppendLine();
        note.AppendLine("## Rows");
        note.AppendLine("| Row | Person | Company | Outcome | Evidence or reason |");
        note.AppendLine("|---|---|---|---|---|");
        foreach (WorkRow row in report.Rows)
        {
            note.AppendLine(RowLine(row, report));
        }
        note.AppendLine();
        note.AppendLine("## Left out by the gates");
        foreach (Dropped dropped in report.Dropped)
        {
            note.AppendLine($"- {dropped.What}: {dropped.Reason}");
        }
        if (report.Answer is { StopCondition: "none", StopDetail: { Length: > 0 } detail })
        {
            note.AppendLine();
            note.AppendLine("## The model's note");
            note.AppendLine(detail);
        }
        if (report.Answer is { StopCondition: not "none" } stopped)
        {
            note.AppendLine();
            note.AppendLine($"## Stop condition: {stopped.StopCondition}");
            note.AppendLine($"{stopped.StopDetail} (screenshot {stopped.StopScreenshot}). Left on screen, nothing attempted.");
        }
        note.AppendLine();
        note.AppendLine(Invariant($"Start {start:HH:mm:ss}, end {end:HH:mm:ss}, wall {(report.Ended - report.Started).TotalMinutes:0.0} minutes."));
        return note.ToString();
    }

    private static string RowLine(WorkRow row, LaneReport report)
    {
        RowResult? result = report.Answer?.Rows.FirstOrDefault(r => r.Row == row.Row);
        SendRecord? send = report.Sends.FirstOrDefault(s => s.Row.Row == row.Row);
        string outcome = send is { Missing.Count: > 0 } ? "sent UNVERIFIED" : result?.Outcome ?? "no result";
        string detail = send is not null
            ? send.Missing.Count == 0 ? $"verified, screenshot {send.Screenshot}" : string.Join("; ", send.Missing)
            : result?.Reason ?? string.Empty;
        string person = result?.Person is { Length: > 0 } found ? found : row.Person;
        return $"| {row.Row} | {Cell(person)} | {Cell(row.Company)} | {outcome} | {Cell(detail)} |";
    }

    private static string Cell(string text) => text.Replace('|', '/').ReplaceLineEndings(" ");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
