using System.Globalization;
using System.Text;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions.Ats;

/// <summary>
/// One pass of the ATS sweep: the known senders' mail still in the inbox, each message decided by
/// AtsRules against the master, the plan written to ats-sweep/last-plan.tsv and one line to
/// ats-sweep/sweep.log. In shadow it stops there, so the plan can be set beside the records run's
/// DONE table; on, it drops each row in inbox/tracker-inbox/ for the merger, then archives, and
/// reads each archived message's labels back. It never sends, drafts, trashes or deletes.
/// </summary>
public sealed class AtsSweep(IMergeStore store, IInbox inbox, TimeProvider time)
{
    /// <summary>The sweep's folder on the share.</summary>
    public const string Folder = "ats-sweep";

    /// <summary>One line a pass.</summary>
    public const string Log = Folder + "/sweep.log";

    /// <summary>The last pass's plan, replaced every pass.</summary>
    public const string LastPlan = Folder + "/last-plan.tsv";

    /// <summary>The spool a cloud writer drops rows in (SHARED-WRITES.md).</summary>
    public const string Spool = "inbox/tracker-inbox";

    /// <summary>At most this many messages a pass, as the records run capped itself.</summary>
    public const int Cap = 150;

    /// <summary>Runs one pass and returns its log line.</summary>
    public async Task<string> RunAsync(bool on, CancellationToken ct)
    {
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)))
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToList();
        var decisions = new List<AtsDecision>();
        foreach (string id in await inbox.FindAsync(Mail.Query, Cap, ct))
        {
            var message = await inbox.ReadAsync(id, ct);
            decisions.Add(AtsRules.Decide(message, await inbox.ThreadSendersAsync(message.ThreadId, ct), master));
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
        await store.WriteAsync(LastPlan, TrackerCsvText.Encode(Tsv(decisions, outcomes, on, Now())), ct);
        string line = $"{(on ? "ON" : "SHADOW")} found {decisions.Count}: "
            + string.Join(", ", Enum.GetValues<AtsAction>().Select(action => $"{Words(action)} {decisions.Count(d => d.Action == action)}"))
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

    private static string Flat(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
