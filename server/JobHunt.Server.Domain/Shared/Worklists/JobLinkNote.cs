using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// The job link in an invite note: always, when it fits. When he has applied to a role at the person's company, the
/// note names the role and carries the posting's link in the approved shape:
/// <code>
/// Charley,
///
/// Your team puts LLM agents into construction workflows. I applied for your Technical Lead Manager role:
///
/// jobs.ashbyhq.com/Homebound/fb4c15fb-11d3-4ab0-9497-720d08051c3f
/// theyard.stevenstout.biz/?r=gupojr
///
/// Is it a bad idea to connect?
///
/// Thanks,
/// Steve
/// </code>
/// The hook is the queued note's first sentence and the proof is the rest of its paragraph. To stay within LinkedIn's
/// 300 (under it, as TextCheck holds every note), "Thanks," goes first, then the proof sentence, then both; a note that still does not fit keeps today's text.
/// The count is LinkedIn's: the text with trailing whitespace stripped (failure-log 7336, R-chat1112 invites).
/// </summary>
public static partial class JobLinkNote
{
    /// <summary>LinkedIn's limit on a note.</summary>
    public const int Limit = 300;

    /// <summary>The ask the approved shape ends on.</summary>
    public const string Ask = "Is it a bad idea to connect?";

    /// <summary>The count LinkedIn's counter shows for the text: its characters with trailing whitespace stripped.</summary>
    public static int Expected(string text) => text.TrimEnd().Length;

    /// <summary>
    /// The posting link an Application row's notes carry, written bare (no scheme, no /application or /confirmation),
    /// for Greenhouse, Ashby and Lever, or null when the row names none of them.
    /// </summary>
    public static string? PostingUrl(string notes) =>
        Posting().Match(notes) is { Success: true } match ? $"{match.Groups["host"].Value}/{match.Groups["path"].Value}" : null;

    /// <summary>True when the text holds a posting link of the three boards; TextCheck lets one through in an invite.</summary>
    public static Regex PostingLink => Posting();

    /// <summary>The note with the role and its link, or null when the queued note is not in the shape this reads or nothing fits.</summary>
    /// <param name="note">The queued note with its code filled in.</param>
    /// <param name="role">The posting's title as its board has it today.</param>
    /// <param name="url">The bare posting link.</param>
    public static string? Build(string note, string role, string url)
    {
        string[] blocks = Blocks().Split(note.Trim());
        if (blocks.Length < 4 || !blocks[0].EndsWith(',') || blocks[0].Contains('\n', StringComparison.Ordinal))
        {
            return null;
        }
        string? link = blocks.FirstOrDefault(b => b.Contains("theyard.stevenstout.biz/?r=", StringComparison.OrdinalIgnoreCase) && !b.Contains('\n', StringComparison.Ordinal));
        if (link is null || blocks[1].Contains("theyard.stevenstout.biz", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string[] sentences = Sentence().Split(blocks[1].Replace('\n', ' ').Trim());
        string hook = sentences[0].Trim();
        string proof = string.Join(' ', sentences.Skip(1)).Trim();
        if (!hook.EndsWith('.'))
        {
            return null;
        }
        foreach (var (withProof, thanks) in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            if (!withProof && proof.Length == 0)
            {
                continue;
            }
            string body = withProof && proof.Length > 0 ? $"{hook} {proof}" : hook;
            string text = $"{blocks[0]}\n\n{body} I applied for your {role.Trim()} role:\n\n{url}\n{link.Trim()}\n\n{Ask}\n\n{(thanks ? "Thanks,\n" : string.Empty)}Steve\n";
            if (Expected(text) < Limit)
            {
                return text;
            }
        }
        return null;
    }

    [GeneratedRegex(@"(?:https?://)?(?<host>job-boards\.greenhouse\.io|boards\.greenhouse\.io|jobs\.ashbyhq\.com|jobs\.lever\.co)/(?<path>[\w.-]+/(?:jobs/)?[\w-]+)(?:/(?:application|confirmation|apply))?", RegexOptions.IgnoreCase)]
    private static partial Regex Posting();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex Blocks();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
    private static partial Regex Sentence();
}
