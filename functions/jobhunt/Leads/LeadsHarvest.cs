using System.Globalization;
using System.Text;
using JobHunt.Functions.Ats;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions.Leads;

/// <summary>
/// One pass of the leads harvest, in shadow: the LinkedIn job alerts still in the inbox, every
/// requisition read out of them, each judged by LeadsFilter, the result written to
/// leads-shadow/last-pass.tsv and one line to leads-shadow/leads.log on the share. It archives nothing
/// and writes no lead file a lane reads, so it can be set beside the records run's leads-&lt;date&gt;.md
/// before it replaces that step.
/// </summary>
public sealed class LeadsHarvest(IMergeStore store, IInbox inbox, TimeProvider time)
{
    /// <summary>The harvest's folder on the share.</summary>
    public const string Folder = "leads-shadow";

    /// <summary>At most this many alerts a pass, as the records run capped itself.</summary>
    public const int Cap = 150;

    /// <summary>Runs one pass and returns its log line.</summary>
    public async Task<string> RunAsync(CancellationToken ct)
    {
        var local = CentralClock.Local(time.GetUtcNow());
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)))
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)).ToList();
        string rules = await store.ExistsAsync("systemRules.md", ct) ? TrackerCsvText.Decode(await store.ReadAsync("systemRules.md", ct)) : string.Empty;
        var leads = new List<Lead>();
        int alerts = 0, unread = 0;
        foreach (string id in await inbox.FindAsync($"in:inbox from:{LinkedInAlert.Sender}", Cap, ct))
        {
            var message = await inbox.ReadAsync(id, ct);
            var (found, missed) = LinkedInAlert.Read(message.Text, id);
            leads.AddRange(found);
            unread += missed;
            alerts++;
        }
        var judged = LeadsFilter.Judge(leads, master, ApplicationCap.DoNotApplyList(rules), DateOnly.FromDateTime(local));
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {local:yyyy-MM-dd HH:mm:ss} Central, shadow, {alerts} alerts, {judged.Count} requisitions, {judged.Count(j => j.Failed is null)} pass\r\n");
        text.Append("Pass\tCompany\tTitle\tLocation\tLink\tGmail id\tWhy not\r\n");
        foreach (var (lead, failed) in judged.OrderBy(j => j.Failed is null ? 0 : 1))
        {
            text.AppendJoin('\t', new[] { failed is null ? "yes" : "no", Flat(lead.Company), Flat(lead.Title), Flat(lead.Location), lead.Link, lead.MessageId, Flat(failed ?? "") }).Append("\r\n");
        }
        await store.EnsureFolderAsync(Folder, ct);
        await store.WriteAsync($"{Folder}/last-pass.tsv", TrackerCsvText.Encode(text.ToString()), ct);
        string line = $"SHADOW alerts {alerts}, requisitions {judged.Count}, pass {judged.Count(j => j.Failed is null)}, blocks unread {unread}";
        await store.AppendAsync($"{Folder}/leads.log", TrackerCsvText.Encode($"{local:yyyy-MM-dd HH:mm:ss} {line}\r\n"), ct);
        return line;
    }

    private static string Flat(string text) => text.Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
