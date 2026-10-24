using System.Globalization;
using System.Text;

namespace JobHunt.Application.Features.Leads;

/// <summary>
/// One pass of the leads harvest: every board's job alerts still in the inbox (BoardAlerts.All: LinkedIn's
/// digest and nudges, Ladders, SuccessFactors agents), every requisition read out of
/// them, each judged by LeadsFilter, the result written to leads-shadow/last-pass.tsv and one line to
/// leads-shadow/leads.log on the share. In shadow it stops there. On, it harvests before it archives: the
/// requisitions of every alert that read whole go to nightly/leads-&lt;date&gt;.md (appended, never rewritten,
/// the file the records run writes), and only then is each of those alerts archived and its labels read back.
/// An alert with a block it could not read, with no job in it, or with anybody but the alert sender in its
/// thread stays in the inbox for the triage run. It never sends, drafts, trashes or deletes.
/// </summary>
public sealed class LeadsHarvest(IMergeStore store, IInbox inbox, TimeProvider time)
{
    /// <summary>The harvest's folder on the share.</summary>
    public const string Folder = "leads-shadow";

    /// <summary>At most this many alerts a pass, as the records run capped itself.</summary>
    public const int Cap = 150;

    /// <summary>The leads the list run has already scored, one link a line first in each row (written by that run, read here).</summary>
    public const string Scored = "leads/scored.tsv";

    /// <summary>The event that fires the leads list run in nightly.yml.</summary>
    public const string ListEvent = "leads";

    /// <summary>After a pass: the links that passed the filter and that the leads list run has not scored yet.</summary>
    public IReadOnlyList<string> Unscored { get; private set; } = [];

    /// <summary>The day's leads file on the share, by Central date: the one the records run appends to.</summary>
    public static string LeadsFile(DateTime local) => $"nightly/leads-{local:yyyy-MM-dd}.md";

    /// <summary>Runs one pass and returns its log line; <paramref name="on"/> harvests to the leads file and archives.</summary>
    public async Task<string> RunAsync(bool on, CancellationToken ct)
    {
        var local = CentralClock.Local(time.GetUtcNow());
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)))
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)).ToList();
        string rules = await store.ExistsAsync("systemRules.md", ct) ? TrackerCsvText.Decode(await store.ReadAsync("systemRules.md", ct)) : string.Empty;
        var alerts = new List<Alert>();
        foreach (var board in BoardAlerts.All)
        {
            // A full harvest asks no more boards: the cap is met, and a search for none is a wasted read, or a 400.
            if (alerts.Count >= Cap)
            {
                break;
            }
            foreach (string id in await inbox.FindAsync($"in:inbox {board.Query}", Cap - alerts.Count, ct))
            {
                if (alerts.Count >= Cap || alerts.Any(a => a.Message.Id == id))
                {
                    continue;
                }
                var message = await inbox.ReadAsync(id, ct);
                var (found, missed) = board.Read(message);
                bool alone = !on || (await inbox.ThreadSendersAsync(message.ThreadId, ct)).All(from => board.Sends(Mail.Address(from)));
                alerts.Add(new Alert(message, found, missed, alone) { Board = board.Board });
            }
        }
        var judged = LeadsFilter.Judge(alerts.SelectMany(a => a.Leads), master, ApplicationCap.DoNotApplyList(rules), DateOnly.FromDateTime(local));
        var verdicts = judged.ToDictionary(j => j.Lead.Link, j => j.Failed, StringComparer.Ordinal);
        var scored = await store.ExistsAsync(Scored, ct)
            ? TrackerCsvText.Decode(await store.ReadAsync(Scored, ct)).Split('\n').Select(row => row.Split('\t')[0].Trim()).ToHashSet(StringComparer.Ordinal)
            : [];
        Unscored = [.. judged.Where(j => j.Failed is null && !scored.Contains(j.Lead.Link)).Select(j => j.Lead.Link)];
        int unread = alerts.Sum(a => a.Unread);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {local:yyyy-MM-dd HH:mm:ss} Central, {(on ? "on" : "shadow")}, {alerts.Count} alerts, {judged.Count} requisitions, {judged.Count(j => j.Failed is null)} pass\r\n");
        text.Append("Pass\tCompany\tTitle\tLocation\tBand\tLink\tGmail id\tWhy not\r\n");
        foreach (var (lead, failed) in judged.OrderBy(j => j.Failed is null ? 0 : 1))
        {
            text.AppendJoin('\t', new[] { failed is null ? "yes" : "no", Flat(lead.Company), Flat(lead.Title), Flat(lead.Location), Flat(lead.Band ?? ""), lead.Link, lead.MessageId, Flat(failed ?? "") }).Append("\r\n");
        }
        await store.EnsureFolderAsync(Folder, ct);
        await store.WriteAsync($"{Folder}/last-pass.tsv", TrackerCsvText.Encode(text.ToString()), ct);
        string line = $"{(on ? "ON" : "SHADOW")} alerts {alerts.Count}, requisitions {judged.Count}, pass {judged.Count(j => j.Failed is null)}, blocks unread {unread}";
        if (on)
        {
            var ready = alerts.Where(a => a.Ready).ToList();
            int archived = 0, failedArchive = 0;
            if (ready.Count > 0)
            {
                await store.EnsureFolderAsync("nightly", ct);
                await store.AppendAsync(LeadsFile(local), TrackerCsvText.Encode(Section(ready, verdicts, local)), ct);
                foreach (var alert in ready)
                {
                    if (await inbox.ArchiveAsync(alert.Message.Id, ct))
                    {
                        archived++;
                    }
                    else
                    {
                        failedArchive++;
                    }
                }
            }
            line += $" | harvested {ready.Count} to {LeadsFile(local)}, {archived} archived and read back, {failedArchive} failed, {alerts.Count - ready.Count} left (a block unread, no job, or another sender in the thread)";
        }
        await store.AppendAsync($"{Folder}/leads.log", TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} {line}\r\n"), ct);
        return line;
    }

    /// <summary>The pass's section of the day's leads file: one table an alert, in the records run's columns plus the filter's verdict.</summary>
    public static string Section(IReadOnlyList<Alert> ready, IReadOnlyDictionary<string, string?> verdicts, DateTime local)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"\n# Leads harvested from board alerts, the 30-minute inbox pass (func-jobhunt-ss, AtsSweep), appended {local:yyyy-MM-dd HH:mm} Central\n\n");
        text.Append("Read from the alerts' plain text by code (functions/JobHunt.Domain/Features/Leads). A listing proves a req existed when the mail was sent, never that it is open now; the apply lane verifies live. Filter: remote, not applied to, clear of RULE 50. Each alert below was archived after this section was written.\n\n");
        text.Append(CultureInfo.InvariantCulture, $"Total: {ready.Sum(a => a.Leads.Count)} requisitions from {ready.Count} alerts.\n");
        foreach (var alert in ready)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {alert.Message.Id} | {alert.Board}, {alert.Message.Address} | {CentralClock.Local(alert.Message.At):yyyy-MM-dd HH:mm} Central | {Cell(alert.Message.Subject)} | {alert.Leads.Count} requisitions\n\n");
            text.Append("| Company | Title | Location / remote line as written | Link | Source Gmail id | Filter |\n|---|---|---|---|---|---|\n");
            foreach (var lead in alert.Leads)
            {
                string verdict = verdicts.TryGetValue(lead.Link, out var failed) && failed is not null ? "no: " + failed : "pass";
                string where = lead.Band is null ? lead.Location : $"{lead.Location}; band {lead.Band}";
                text.Append(CultureInfo.InvariantCulture, $"| {Cell(lead.Company)} | {Cell(lead.Title)} | {Cell(where)} | {lead.Link} | {lead.MessageId} | {Cell(verdict)} |\n");
            }
        }
        return text.ToString();
    }

    private static string Flat(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Cell(string text) => Flat(text).Replace("|", "/", StringComparison.Ordinal).Trim();

    /// <summary>One alert as the pass read it.</summary>
    /// <param name="Message">The mail.</param>
    /// <param name="Leads">The requisitions read out of it.</param>
    /// <param name="Unread">The blocks that did not read as a requisition.</param>
    /// <param name="Alone">True when every message in its thread is from the alert sender.</param>
    public sealed record Alert(InboxMessage Message, IReadOnlyList<Lead> Leads, int Unread, bool Alone)
    {
        /// <summary>The board that sent it, as BoardAlerts names it.</summary>
        public string Board { get; init; } = "LinkedIn digest";

        /// <summary>Harvest and archive only an alert that read whole, holds a job, and has no other sender in its thread.</summary>
        public bool Ready => Alone && Unread == 0 && Leads.Count > 0;
    }
}
