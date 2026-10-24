namespace JobHunt.Application.Features.Ats;

/// <summary>
/// The Gmail inbox as the ATS sweep sees it: messages found by a query, one message read whole, the
/// senders of its thread, and an archive that removes the INBOX label. There is no fourth method on
/// purpose: nothing here can send, draft, trash or delete (the red line in BOOKKEEPING-LANE.md), and
/// AtsSweepTests holds the port to these four.
/// </summary>
public interface IInbox
{
    /// <summary>The ids of the messages a Gmail search finds, newest first, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct);

    /// <summary>One message: its headers, its date and its text.</summary>
    Task<InboxMessage> ReadAsync(string id, CancellationToken ct);

    /// <summary>The From address of every message in a thread.</summary>
    Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct);

    /// <summary>Removes the INBOX label from one message, and returns true when its labels read back without it.</summary>
    Task<bool> ArchiveAsync(string id, CancellationToken ct);

    /// <summary>True when a draft sits in the thread, which is a reply already waiting for Steve's word; an inbox that cannot tell says no.</summary>
    Task<bool> ThreadHasDraftAsync(string threadId, CancellationToken ct) => Task.FromResult(false);

    /// <summary>
    /// The From of the newest message in a thread that is not a draft, or null for an empty thread: when it is Steve,
    /// the thread is answered. An inbox that lists a thread's senders oldest first answers it from that list.
    /// </summary>
    async Task<string?> ThreadNewestSenderAsync(string threadId, CancellationToken ct) =>
        (await ThreadSendersAsync(threadId, ct)).LastOrDefault();
}
