namespace JobHunt.Application.Features.NetworkLog;

/// <summary>Which threads a pass reads: those touched in the last days, at most a number of them, never chats.</summary>
/// <param name="NewerThanDays">The window in days.</param>
/// <param name="Max">The most thread ids returned.</param>
public sealed record ThreadSearch(int NewerThanDays, int Max);

/// <summary>One pass's row in network.Passes.</summary>
/// <param name="CentralDay">The Central day the pass counts for.</param>
/// <param name="Mode">shadow or on.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="FinishedAt">When it finished, null while running or after a crash.</param>
public sealed record PassRow(DateOnly CentralDay, string Mode, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);

/// <summary>A finished pass's counts, written to its row and its line.</summary>
/// <param name="ThreadsRead">Threads read.</param>
/// <param name="Upserted">Rows written or moved.</param>
/// <param name="Stopped">Rows stopped or closed.</param>
/// <param name="Quiet">Rows made Quiet.</param>
/// <param name="DraftsBuilt">Drafts written (or, in shadow, would-drafts).</param>
/// <param name="StartedThisWeek">Conversations started this ISO week.</param>
/// <param name="Seconds">How long the pass took.</param>
/// <param name="Line">The pass line as written to the share.</param>
public sealed record PassCounts(int ThreadsRead, int Upserted, int Stopped, int Quiet, int DraftsBuilt, int StartedThisWeek, int Seconds, string Line);

/// <summary>The network log's rows, read: by person, by the cross-channel key, and the open set.</summary>
public interface IConversationReader
{
    /// <summary>The row stored under the key, or null.</summary>
    Task<Conversation?> FindAsync(string personKey, CancellationToken ct);

    /// <summary>The row whose cross-channel key is the one given, or null.</summary>
    Task<Conversation?> FindByMatchKeyAsync(string matchKey, CancellationToken ct);

    /// <summary>Every row a pass works over: Open and Drafted.</summary>
    Task<IReadOnlyList<Conversation>> OpenAsync(CancellationToken ct);

    /// <summary>Every row's first two-way time, for the week's scoreboard.</summary>
    Task<IReadOnlyList<DateTimeOffset>> StartsAsync(CancellationToken ct);

    /// <summary>The pass row of a Central day, or null when none began.</summary>
    Task<PassRow?> PassAsync(DateOnly centralDay, CancellationToken ct);
}

/// <summary>The network log's rows, written. The Function is the one writer of network.Conversations.</summary>
public interface IConversationStore : IConversationReader
{
    /// <summary>Inserts the row, or replaces the stored row with the same PersonKey, stamped with the pass's time; answers its id.</summary>
    Task<long> UpsertAsync(Conversation row, DateTimeOffset at, CancellationToken ct);

    /// <summary>Moves a row to a state, with the reason for a Closed or Stopped row.</summary>
    Task SetStateAsync(long id, ConversationState state, string? reason, CancellationToken ct);

    /// <summary>Stores a row's TopicLine and the file it came from.</summary>
    Task SetTopicAsync(long id, string topicLine, string source, CancellationToken ct);

    /// <summary>Marks a row Drafted with the draft's id and channel.</summary>
    Task SetDraftAsync(long id, string draftId, Channel channel, DateTimeOffset at, CancellationToken ct);

    /// <summary>Writes the start of a Central day's pass, replacing a crashed one.</summary>
    Task StartPassAsync(DateOnly centralDay, string mode, DateTimeOffset at, CancellationToken ct);

    /// <summary>Writes a pass's finish and counts.</summary>
    Task FinishPassAsync(DateOnly centralDay, DateTimeOffset at, PassCounts counts, CancellationToken ct);
}

/// <summary>Gmail threads, read as metadata: the network log never reads a body it does not need.</summary>
public interface IThreadReader
{
    /// <summary>The ids of the threads the search finds, newest first.</summary>
    Task<IReadOnlyList<string>> FindThreadIdsAsync(ThreadSearch search, CancellationToken ct);

    /// <summary>The thread's subject and its messages' senders, times, labels and opening words.</summary>
    Task<ThreadSummary> ReadSummaryAsync(string threadId, CancellationToken ct);

    /// <summary>Whether any message in the thread carries a text/calendar part.</summary>
    Task<bool> ReadHasCalendarPartAsync(string threadId, CancellationToken ct);
}

/// <summary>A draft as the network log writes it into a thread.</summary>
/// <param name="ThreadId">The thread the draft is written into.</param>
/// <param name="To">The person's address, read off a message they sent.</param>
/// <param name="Subject">Re: and the original subject.</param>
/// <param name="Text">The plain body.</param>
/// <param name="Html">The HTML body.</param>
/// <param name="InReplyTo">The newest message's Message-Id.</param>
/// <param name="References">The References header, the same id.</param>
public sealed record DraftMail(string ThreadId, string To, string Subject, string Text, string Html, string? InReplyTo, string? References);

/// <summary>The Gmail draft port: drafts and labels only, never a send. Shadow is handed a writer that refuses.</summary>
public interface IDraftWriter
{
    /// <summary>The label's id, made when absent.</summary>
    Task<string> EnsureLabelAsync(string name, CancellationToken ct);

    /// <summary>Writes the draft into its thread; answers the draft's id and its message's id.</summary>
    Task<(string DraftId, string MessageId)> CreateDraftAsync(DraftMail draft, CancellationToken ct);

    /// <summary>Puts a label on a message.</summary>
    Task LabelAsync(string messageId, string labelId, CancellationToken ct);

    /// <summary>Whether the draft is still in Drafts.</summary>
    Task<bool> DraftExistsAsync(string draftId, CancellationToken ct);
}

/// <summary>The threads the campaign still owns: a real send with no reply yet belongs to the campaign, not here.</summary>
public interface ICampaignOwnership
{
    /// <summary>The Gmail thread ids of the campaign's open sends.</summary>
    Task<IReadOnlySet<string>> OpenThreadIdsAsync(CancellationToken ct);
}
