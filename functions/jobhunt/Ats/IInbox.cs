namespace JobHunt.Functions.Ats;

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
}

/// <summary>One message as the sweep reads it.</summary>
/// <param name="Id">The Gmail message id.</param>
/// <param name="ThreadId">The Gmail thread id.</param>
/// <param name="From">The From header as written.</param>
/// <param name="Subject">The Subject header.</param>
/// <param name="At">When Gmail received it.</param>
/// <param name="Text">The body as plain text, markup removed.</param>
public sealed record InboxMessage(string Id, string ThreadId, string From, string Subject, DateTimeOffset At, string Text)
{
    /// <summary>
    /// The body with its line breaks kept, for a reader that goes line by line (a job alert's blocks);
    /// Text joins every line into one, which is what the ATS rules' phrases need.
    /// </summary>
    public string Lines { get; init; } = "";

    /// <summary>The HTML parts as sent, for a reader that needs a link's address (a SuccessFactors agent has no plain part).</summary>
    public string Html { get; init; } = "";

    /// <summary>The address in the From header, lower case.</summary>
    public string Address => Mail.Address(From);

    /// <summary>The display name in the From header, or empty.</summary>
    public string Name => Mail.Name(From);
}
