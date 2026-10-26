using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace JobHunt.Application.Features.Ats;

/// <summary>
/// One pass of the ATS sweep: the known senders' mail still in the inbox, each message decided by
/// AtsRules against the master, the plan written to ats-sweep/last-plan.tsv and one line to
/// ats-sweep/sweep.log. In shadow it stops there, so the plan can be set beside the records run's
/// DONE table; on, it drops each row in inbox/tracker-inbox/ for the merger, then archives, and
/// reads each archived message's labels back. It never sends, drafts, trashes or deletes. A message it left in the
/// inbox before is not read again while its thread's senders and the tracker are what they were (<see cref="Judged"/>).
/// </summary>
public sealed class AtsSweep(IMergeStore store, IInbox inbox, TimeProvider time)
{
    /// <summary>The sweep's folder on the share.</summary>
    public const string Folder = "ats-sweep";

    /// <summary>One line a pass.</summary>
    public const string Log = Folder + "/sweep.log";

    /// <summary>The last pass's plan, replaced every pass.</summary>
    public const string LastPlan = Folder + "/last-plan.tsv";

    /// <summary>
    /// The messages the last pass left in the inbox, one line each: the Gmail id, its thread, the thread's senders and
    /// the tracker's hash when it was judged. Replaced every pass with the messages still left.
    /// </summary>
    public const string Judged = Folder + "/judged.tsv";

    /// <summary>The spool a cloud writer drops rows in (SHARED-WRITES.md).</summary>
    public const string Spool = "inbox/tracker-inbox";

    /// <summary>At most this many messages a pass, as the records run capped itself.</summary>
    public const int Cap = 150;

    /// <summary>The known tracking systems' mail still in the inbox.</summary>
    public static MailSearch Search { get; } = MailSearch.InInboxFrom(Mail.AtsDomains);

    /// <summary>Runs one pass and returns its log line.</summary>
    public async Task<string> RunAsync(bool on, CancellationToken ct)
    {
        byte[] masterBytes = await store.ReadAsync(MergePaths.Master, ct);
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(masterBytes))
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToList();
        string tracker = Convert.ToHexStringLower(SHA256.HashData(masterBytes));
        var before = await JudgedAsync(ct);
        var left = new List<JudgedLine>();
        var decisions = new List<AtsDecision>();
        int unchanged = 0;
        foreach (string id in await inbox.FindAsync(Search, Cap, ct))
        {
            if (before.TryGetValue(id, out var was) && was.Tracker == tracker)
            {
                string senders = Senders(await inbox.ThreadSendersAsync(was.Thread, ct));
                if (senders == was.Senders)
                {
                    // Left before, nothing it was judged on has moved: the same decision, without reading it again.
                    left.Add(was);
                    unchanged++;
                    continue;
                }
            }
            var message = await inbox.ReadAsync(id, ct);
            var threadSenders = await inbox.ThreadSendersAsync(message.ThreadId, ct);
            var decision = AtsRules.Decide(message, threadSenders, master);
            decisions.Add(decision);
            if (decision.Action == AtsAction.Leave)
            {
                left.Add(new JudgedLine(id, message.ThreadId, Senders(threadSenders), tracker));
            }
        }
        var outcomes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (on)
        {
            foreach (var decision in decisions)
            {
                outcomes[decision.Message.Id] = await ActAsync(decision, ct);
            }
        }
        await store.EnsureFolderAsync(Folder, ct);
        await store.WriteAsync(LastPlan, TrackerCsvText.Encode(Tsv(decisions, outcomes, on, Now()) + (unchanged == 0 ? "" : $"# and {unchanged} left before and not read again: their threads and the tracker are unchanged ({Judged})\r\n")), ct);
        await store.WriteAsync(Judged, TrackerCsvText.Encode(string.Concat(left.Select(j => $"{j.Id}\t{j.Thread}\t{j.Senders}\t{j.Tracker}\r\n"))), ct);
        string line = $"{(on ? "ON" : "SHADOW")} found {decisions.Count + unchanged}: "
            + string.Join(", ", Enum.GetValues<AtsAction>().Select(action => $"{Words(action)} {decisions.Count(d => d.Action == action) + (action == AtsAction.Leave ? unchanged : 0)}"))
            + (unchanged == 0 ? "" : $" ({unchanged} left before, not read again)")
            + (on ? $" | {outcomes.Values.Count(v => v.EndsWith("archived", StringComparison.Ordinal))} archived and read back, {outcomes.Values.Count(v => v.Contains("FAILED", StringComparison.Ordinal))} failed" : "");
        await store.AppendAsync(Log, TrackerCsvText.Encode($"{Now():yyyy-MM-dd HH:mm:ss} {line}\r\n"), ct);
        return line;
    }

    /// <summary>Does what one decision says: the row first, then the archive, read back.</summary>
    private async Task<string> ActAsync(AtsDecision decision, CancellationToken ct)
    {
        string done = "";
        if (decision.Row is { } row)
        {
            string name = $"{time.GetUtcNow():yyyyMMdd-HHmmss}Z-atssweep-{decision.Message.Id}.csv";
            await store.EnsureFolderAsync(Spool, ct);
            await store.WriteAsync($"{Spool}/{name}", TrackerCsvText.Encode(TrackerCsvText.Line(row) + "\r\n"), ct);
            done = $"row {name}; ";
        }
        if (decision.Action is AtsAction.ArchiveOnly or AtsAction.RowAndArchive)
        {
            return done + (await inbox.ArchiveAsync(decision.Message.Id, ct) ? "archived" : "archive FAILED: INBOX still on it");
        }
        return done + "left in the inbox";
    }

    /// <summary>The plan as a tab-separated file: one line a message, with what was done when on.</summary>
    public static string Tsv(IReadOnlyList<AtsDecision> decisions, IReadOnlyDictionary<string, string> outcomes, bool on, DateTime local)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {local:yyyy-MM-dd HH:mm:ss} Central, {(on ? "on" : "shadow")}, {decisions.Count} messages from known senders in the inbox\r\n");
        text.Append("Gmail id\tReceived\tFrom\tSubject\tKind\tCompany\tAction\tRests on\tWhy\tRow notes\tDone\r\n");
        foreach (var d in decisions)
        {
            text.AppendJoin('\t', new[]
            {
                d.Message.Id, CentralClock.Local(d.Message.At).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), d.Message.Address, Flat(d.Message.Subject),
                d.Kind.ToString(), d.Company ?? "", d.Action.ToString(), d.RestsOn ?? "", Flat(d.Why), Flat(d.Row?[5] ?? ""), outcomes.GetValueOrDefault(d.Message.Id, ""),
            }).Append("\r\n");
        }
        return text.ToString();
    }

    private static string Words(AtsAction action) => action switch
    {
        AtsAction.Leave => "left",
        AtsAction.ArchiveOnly => "archive only",
        AtsAction.RowAndArchive => "row and archive",
        _ => "other",
    };

    private DateTime Now() => CentralClock.Local(time.GetUtcNow());

    /// <summary>The messages the last pass left, by Gmail id; a file that is missing or a line that does not read is no line.</summary>
    private async Task<Dictionary<string, JudgedLine>> JudgedAsync(CancellationToken ct)
    {
        var lines = new Dictionary<string, JudgedLine>(StringComparer.Ordinal);
        if (!await store.ExistsAsync(Judged, ct))
        {
            return lines;
        }
        foreach (string text in TrackerCsvText.Decode(await store.ReadAsync(Judged, ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] cells = text.Split('\t');
            if (cells.Length == 4)
            {
                lines[cells[0]] = new JudgedLine(cells[0], cells[1], cells[2], cells[3]);
            }
        }
        return lines;
    }

    /// <summary>A thread's senders as one comparable cell: lower case, sorted, once each, no tabs.</summary>
    private static string Senders(IReadOnlyList<string> senders) =>
        Flat(string.Join(",", senders.Select(s => s.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));

    private sealed record JudgedLine(string Id, string Thread, string Senders, string Tracker);

    private static string Flat(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
