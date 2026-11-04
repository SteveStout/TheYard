using System.Globalization;
using System.Text;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>A pass's counts as its line on the share says them.</summary>
/// <param name="Mode">shadow or on.</param>
/// <param name="Threads">Threads read.</param>
/// <param name="Upserted">Rows written or moved.</param>
/// <param name="Stopped">Rows stopped or closed.</param>
/// <param name="Quiet">Rows made Quiet.</param>
/// <param name="Personal">Two-way threads with a personal address nobody in the job hunt knows, left out.</param>
/// <param name="Due">Rows owed a follow-up this Monday.</param>
/// <param name="Drafted">Drafts written, or in shadow would-drafts.</param>
/// <param name="Seconds">How long the pass took.</param>
/// <param name="StartedThisWeek">Conversations started this ISO week.</param>
/// <param name="Extra">Further counts, in the order given, after the standard ones.</param>
public sealed record PassLine(string Mode, int Threads, int Upserted, int Stopped, int Quiet, int Personal, int Due, int Drafted, int Seconds, int StartedThisWeek, IReadOnlyList<(string Name, int Value)> Extra);

/// <summary>
/// The network log's files on the share and their text: the pass log, the TopicLine export the 4 AM lane reads, the
/// shadow's would-drafts, and the LinkedIn spool the 6 AM program writes. The share is private, so the would-drafts
/// carry the rendered text; the pass log carries counts only.
/// </summary>
public static class NetworkFiles
{
    /// <summary>The feature's folder on the share.</summary>
    public const string Folder = "networklog";

    /// <summary>One line a pass.</summary>
    public const string PassLog = "networklog/pass.log";

    /// <summary>The rows waiting on a TopicLine, rewritten whole each pass.</summary>
    public const string TopicsNeeded = "networklog/topics-needed.tsv";

    /// <summary>The shadow's would-drafts, one file a Monday.</summary>
    public const string WouldDrafts = "networklog/would-drafts";

    /// <summary>Where the 6 AM program leaves LinkedIn rows.</summary>
    public const string LinkedInInbox = "networklog/linkedin-inbox";

    /// <summary>The pass line: the Central time, the counts, and the week's scoreboard last.</summary>
    public static string Line(DateTime centralNow, PassLine p)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{centralNow:yyyy-MM-dd HH:mm:ss} mode={p.Mode} threads={p.Threads} upserted={p.Upserted} stopped={p.Stopped} quiet={p.Quiet} personal={p.Personal} due={p.Due} drafted={p.Drafted}");
        foreach (var (name, value) in p.Extra)
        {
            text.Append(CultureInfo.InvariantCulture, $" {name}={value}");
        }
        return text.Append(CultureInfo.InvariantCulture, $" seconds={p.Seconds} conversations started this ISO week: {p.StartedThisWeek}").ToString();
    }

    /// <summary>The TopicLine export: a header and one row per Open row with no TopicLine, tabs and line breaks taken out of every field.</summary>
    public static string TopicsNeededText(IEnumerable<Conversation> rows)
    {
        var text = new StringBuilder("PersonKey\tChannel\tThreadIds\tFirstName\tCompany\tLastSubject\tTextPath\n");
        foreach (var row in rows.Where(r => r.State == ConversationState.Open && string.IsNullOrWhiteSpace(r.TopicLine)).OrderBy(r => r.PersonKey, StringComparer.Ordinal))
        {
            string textPath = row.Channel == Channel.LinkedIn && row.ThreadIds.Count > 0 ? $"{LinkedInInbox}/text/{row.ThreadIds[0]}.txt" : "";
            string[] fields = [row.PersonKey, row.Channel.ToString(), string.Join(' ', row.ThreadIds), row.FirstName, row.Company ?? "", row.LastSubject ?? "", textPath];
            text.Append(string.Join('\t', fields.Select(Clean))).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>The shadow's would-drafts for a Monday: one block a row, its key, its address and the rendered text, or the check it failed.</summary>
    public static string WouldDraftsText(DateOnly monday, IEnumerable<(Conversation Row, FollowUpDraft Draft, IReadOnlyList<string> Failed)> drafts)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"# Would-drafts for Monday {monday:yyyy-MM-dd} ({DraftTemplate.Version}, shadow: nothing was written to Gmail)\n");
        foreach (var (row, draft, failed) in drafts)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {row.PersonKey}\n\n- To: {row.Email ?? "(LinkedIn)"}\n- Thread: {(row.ThreadIds.Count > 0 ? row.ThreadIds[0] : "")}\n- Channel: {row.LastChannel}\n");
            text.Append(failed.Count == 0 ? "- Checks: pass\n" : $"- Checks: FAILED {string.Join(", ", failed)}\n");
            text.Append(CultureInfo.InvariantCulture, $"\nSubject: {draft.Subject}\n\n{draft.Text}\n");
        }
        return text.ToString();
    }

    /// <summary>The would-drafts file of a Monday.</summary>
    public static string WouldDraftsPath(DateOnly monday) => $"{WouldDrafts}/{monday:yyyy-MM-dd}.md";

    private static string Clean(string field) => field.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
