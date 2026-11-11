using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Ats;

/// <summary>What the job hunt already knows of the people it writes to, read from the tracker.</summary>
/// <param name="Addresses">Every address a tracker row holds, lower case.</param>
/// <param name="Domains">The work domains of those addresses, personal mail domains left out.</param>
public sealed record KnownContacts(IReadOnlySet<string> Addresses, IReadOnlySet<string> Domains);

/// <summary>A message a person wrote on a job thread, as the reply pass keeps it for its draft.</summary>
/// <param name="Id">The Gmail message id.</param>
/// <param name="ThreadId">The Gmail thread id.</param>
/// <param name="To">The person's address, the draft's only recipient.</param>
/// <param name="Name">The person's display name, or empty.</param>
/// <param name="Subject">The message's subject.</param>
/// <param name="At">When Gmail received it.</param>
/// <param name="Text">The body as plain text, which the item drafts the reply to.</param>
public sealed record WaitingReply(string Id, string ThreadId, string To, string Name, string Subject, DateTimeOffset At, string Text);

/// <summary>
/// A reply reaches Steve with a draft: a message a person wrote on a job thread (one whose writer's address, or work
/// domain, a tracker row already holds) gets one dispatcher item, on Sonnet, that writes the reply
/// in his voice to a file on the share and one line on TODAY.md. The Function puts that text into the thread's Gmail
/// drafts with a writer that has no send, and only when POUNCE_RUN says live. Nothing on this path sends: the draft
/// waits for Steve's own "send".
/// </summary>
public static partial class ReplyDraft
{
    /// <summary>The markers, the drafts the items write, and what was placed in Gmail.</summary>
    public const string Folder = "inbox-pass/reply-drafts";

    /// <summary>One line a message queued or placed.</summary>
    public const string Log = "inbox-pass/reply-drafts.log";

    /// <summary>The dispatcher's ready folder on the share.</summary>
    public const string Ready = "dispatch/ready";

    /// <summary>How far back a message is read: a pass that missed some firings still finds it.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(2);

    /// <summary>The message's marker, written before its item, so a message gets one item.</summary>
    public static string Marker(string id) => $"{Folder}/{id}.md";

    /// <summary>The draft text the item writes.</summary>
    public static string DraftPath(string id) => $"{Folder}/{id}.draft.md";

    /// <summary>Written once the draft is in Gmail, holding its draft id.</summary>
    public static string PlacedPath(string id) => $"{Folder}/{id}.placed";

    /// <summary>The item's lane.</summary>
    public static string Lane(string id) => $"replydraft-{id}";

    /// <summary>The item's path; 12 puts it after a first touch's wrapper and before a follow-up.</summary>
    public static string ReadyPath(string id) => $"{Ready}/12-{Lane(id)}.md";

    /// <summary>
    /// The addresses and work domains the job hunt already knows, from the tracker's Phone/Email cells: every address a
    /// row holds, and the registrable domain of each one that is not a personal mail domain, so a colleague of a known
    /// contact counts and a stranger on gmail.com never does.
    /// </summary>
    /// <param name="tracker">The tracker's rows, the header among them or not.</param>
    public static KnownContacts Known(IReadOnlyList<string[]> tracker)
    {
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var domains = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] row in tracker.Where(row => row.Length >= 6))
        {
            foreach (Match found in Address().Matches(row[5]))
            {
                string address = found.Value.ToLowerInvariant();
                addresses.Add(address);
                if (CompanyKey.Of(address) is { Length: > 0 } key && !PersonalDomains.All.Contains(key))
                {
                    domains.Add(key);
                }
            }
        }
        return new KnownContacts(addresses, domains);
    }

    /// <summary>
    /// A job thread: its writer's address is one a tracker row holds, or its work domain is the domain of one. Any other
    /// thread, a personal one Steve wrote in among them, is not, whatever its subject says.
    /// </summary>
    /// <param name="address">The address of the person who wrote the message.</param>
    /// <param name="known">What the tracker knows.</param>
    public static bool IsJobThread(string address, KnownContacts known)
    {
        string to = address.Trim().ToLowerInvariant();
        return known.Addresses.Contains(to) || (CompanyKey.Of(to) is { Length: > 0 } key && !PersonalDomains.All.Contains(key) && known.Domains.Contains(key));
    }

    /// <summary>Written for a message that was not on a job thread, so the log says so once and the pass never asks again.</summary>
    public static string SkippedPath(string id) => $"{Folder}/{id}.skipped";

    /// <summary>The reply's subject: the message's, with one Re: in front.</summary>
    public static string ReplySubject(string subject) =>
        subject.TrimStart().StartsWith("re:", StringComparison.OrdinalIgnoreCase) ? subject.Trim() : $"Re: {subject.Trim()}";

    /// <summary>The most of a message's text the marker keeps for the item.</summary>
    public const int TextCap = 4000;

    /// <summary>
    /// The marker's text: one name=value field a line, which the placing pass reads back, then a line of three dashes and the
    /// message's own text, which the item drafts to, so the item never opens a mailbox.
    /// </summary>
    public static string MarkerText(WaitingReply reply, DateTimeOffset now) =>
        $"id={reply.Id}\nthread={reply.ThreadId}\nto={reply.To}\nsubject={ReplySubject(reply.Subject)}\nreceived={reply.At:O}\nqueued={now:O}\n---\n"
        + (reply.Text.Length > TextCap ? reply.Text[..TextCap] : reply.Text) + "\n";

    /// <summary>The thread, recipient and subject a marker holds, or null when it does not read.</summary>
    public static (string Thread, string To, string Subject)? ReadMarker(string text)
    {
        var fields = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').TakeWhile(line => line != "---")
            .Select(line => line.Split('=', 2)).Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0], StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.Ordinal);
        return fields.TryGetValue("thread", out var thread) && fields.TryGetValue("to", out var to) && fields.TryGetValue("subject", out var subject)
            && thread.Length > 0 && to.Contains('@', StringComparison.Ordinal) ? (thread, to, subject) : null;
    }

    /// <summary>The draft's body as the item wrote it, trimmed, or null while it is empty.</summary>
    public static string? Body(string draft)
    {
        string text = draft.Replace("\r", "", StringComparison.Ordinal).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>The item: a header the dispatcher reads, then the steps a Sonnet lane follows. It opens no mailbox and sends nothing.</summary>
    /// <param name="reply">The message.</param>
    /// <param name="live">True when the Function will put the draft into Gmail; false writes it to the DONE note only.</param>
    /// <param name="at">When the pass found it.</param>
    public static string Item(WaitingReply reply, bool live, DateTimeOffset at)
    {
        string lane = Lane(reply.Id);
        string stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";
        string done = $@"J:\inbox-pass\reply-drafts\{stamp}-{reply.Id}-DONE.md";
        string draft = $"/mnt/j/{DraftPath(reply.Id)}";
        string who = reply.Name.Length > 0 ? $"{reply.Name} <{reply.To}>" : reply.To;
        string received = CentralClock.Local(reply.At).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var item = new StringBuilder();
        item.Append($"Lane: {lane}\nModel: claude-sonnet-5-5\nEffort: medium\nHolds:\nNeeds: jdrive\nStall_minutes: 20\nMax_hours: 1\nScope: jobhunt\n");
        item.Append($"Entered_by: the Function's reply pass, which read message {reply.Id} at {CentralClock.Local(at):yyyy-MM-dd HH:mm} Central\n");
        item.Append($"Approved_by: the pounce plan, a reply reaches Steve with a draft ({(live ? "live: the Function puts the draft in Gmail, nothing sends" : "dry run: the draft stays in the DONE note")})\n");
        item.Append($"Done_note: {done}\n\n");
        item.Append($"# {lane}: a draft for one reply from a person\n\n");
        item.Append("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n");
        item.Append("## The message\n\n");
        item.Append($"From {who}, received {received} Central, subject \"{reply.Subject}\", Gmail thread {reply.ThreadId}. Its text is in /mnt/j/{Marker(reply.Id)}, under the line of three dashes, as the Function read it.\n\n");
        item.Append("## What you are\n\n");
        item.Append($"A drafter. You open no browser and no mailbox, send nothing and draft nothing in Gmail. You write only {draft}, one line on /mnt/j/TODAY.md, your DONE note and your CLOSED row. Nothing here is ever sent: the draft waits for Steve to say send.\n\n");
        item.Append("## Steps (on this box J:\\ is /mnt/j)\n\n");
        item.Append("1. Read /mnt/j/writing-samples.md and /mnt/j/standard-answers.md for his voice and his settled answers, and the tracker's rows for this person and company for the history.\n");
        item.Append($"2. Write the reply's body, and nothing else, to {draft}.tmp, then rename it to {draft} with one `mv`, so the Function never reads half a draft: plain text, his voice, at most 120 words, no em dash, no link to the Admin tab, every claim from the banks, and any question the banks do not answer left as a bracketed note to Steve.\n");
        item.Append($"3. Append one line to /mnt/j/TODAY.md: `- REPLY DRAFT {received}: {who}, \"{reply.Subject}\": {(live ? "a draft waits in that Gmail thread for your send" : "dry run, the draft is in the DONE note")}`.\n");
        item.Append($"4. Write the DONE note at {done}, the RULES FIRST line on top: the draft word for word, its word count, and `## LEFT FOR STEVE` with `- read the draft and say send, or change it`.\n");
        item.Append($"Last, write your CLOSED row through the state.md spool: a new file /mnt/j/inbox/append-inbox/state.md/<UTC yyyyMMdd-HHmmss>Z-{lane}-close.md holding one line that begins `## LANE: {lane} CLOSED <HH:MM> CST`, says `Holds: none` and the DONE note's path, and ends ` | CLOSED |`. Then stop.\n");
        return item.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Address();
}
