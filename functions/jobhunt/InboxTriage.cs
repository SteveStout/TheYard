using JobHunt.Functions.Ats;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions;

/// <summary>
/// What the inbox pass's code leaves for the model: the inbox's mail from the last two days that no triage
/// run has judged. The triage run (nightly.yml, mode triage, nightly/TRIAGE-LANE.md on the share) judges each
/// one, acts on the alerts and rejections, leaves the rest, and writes one line a message to triage/seen.tsv,
/// the Gmail id first. So the model is started only when there is mail it has not seen, and a message it left
/// for Steve is never judged twice. Reading only: nothing here archives, labels or writes to the inbox.
/// </summary>
public sealed class InboxTriage(IMergeStore store, IInbox inbox)
{
    /// <summary>The repository_dispatch type nightly.yml runs the triage lane for.</summary>
    public const string Event = "triage";

    /// <summary>The triage run's folder on the share.</summary>
    public const string Folder = "triage";

    /// <summary>One line a judged message, written by the triage run: the Gmail id, then what it decided and why.</summary>
    public const string Seen = Folder + "/seen.tsv";

    /// <summary>The window the triage run reads.</summary>
    public const string Query = "in:inbox newer_than:2d";

    /// <summary>At most this many ids a pass.</summary>
    public const int Cap = 100;

    /// <summary>The ids in the window that triage/seen.tsv does not name, newest first.</summary>
    public async Task<IReadOnlyList<string>> UnjudgedAsync(CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (await store.ExistsAsync(Seen, ct))
        {
            foreach (string line in TrackerCsvText.Decode(await store.ReadAsync(Seen, ct)).Split('\n'))
            {
                string id = line.Split('\t')[0].Trim();
                if (id.Length > 0 && !id.StartsWith('#'))
                {
                    seen.Add(id);
                }
            }
        }
        return [.. (await inbox.FindAsync(Query, Cap, ct)).Where(id => !seen.Contains(id))];
    }
}
