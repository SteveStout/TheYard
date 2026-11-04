namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>Where a conversation happened: a Gmail thread, or a LinkedIn Messaging thread the 6 AM program read.</summary>
public enum Channel
{
    /// <summary>A Gmail thread on the connected account.</summary>
    Email,

    /// <summary>A LinkedIn Messaging thread.</summary>
    LinkedIn,
}

/// <summary>Where a conversation stands. Only Open is ever drafted.</summary>
public enum ConversationState
{
    /// <summary>Two-way and not yet followed up, or answered since the last follow-up.</summary>
    Open,

    /// <summary>A follow-up draft is waiting, or was sent and the 21 days of the quiet rule are still running.</summary>
    Drafted,

    /// <summary>A sent follow-up met 21 days of silence: never drafted again until the person writes.</summary>
    Quiet,

    /// <summary>The person asked to stop: never drafted again.</summary>
    Stopped,

    /// <summary>Closed with a reason (stale, bounce, declined): never drafted again until the person writes.</summary>
    Closed,
}

/// <summary>
/// One person Steve has actually talked to, across both channels: the row the network log keeps in
/// network.Conversations. Every instant is UTC; the rules read them against a clock they are handed.
/// </summary>
public sealed record Conversation
{
    /// <summary>The row's id in the store, 0 before it is saved.</summary>
    public long Id { get; init; }

    /// <summary>The lower-case address for an email row, linkedin:&lt;slug&gt; for a LinkedIn row (PersonKey).</summary>
    public required string PersonKey { get; init; }

    /// <summary>first last|company, the key that joins one person across channels (MatchKey), or null when no company is known.</summary>
    public string? MatchKey { get; init; }

    /// <summary>The first name the person signs with, the draft's greeting.</summary>
    public required string FirstName { get; init; }

    /// <summary>The whole name as the person's own message carried it.</summary>
    public required string FullName { get; init; }

    /// <summary>The company as read (a domain's first label for email, the profile's company for LinkedIn).</summary>
    public string? Company { get; init; }

    /// <summary>The company's registrable domain, the way CompanyKey keys it, when the row came from mail.</summary>
    public string? CompanyKey { get; init; }

    /// <summary>The channel the row was first read on.</summary>
    public Channel Channel { get; init; }

    /// <summary>The address the person wrote from: the only address a draft is ever written to.</summary>
    public string? Email { get; init; }

    /// <summary>The person's LinkedIn profile, for a LinkedIn row.</summary>
    public string? ProfileUrl { get; init; }

    /// <summary>The thread ids the conversation was read from, newest exchange first.</summary>
    public IReadOnlyList<string> ThreadIds { get; init; } = [];

    /// <summary>When both sides had first written.</summary>
    public DateTimeOffset FirstTwoWayAt { get; init; }

    /// <summary>The person's newest message.</summary>
    public DateTimeOffset? LastInboundAt { get; init; }

    /// <summary>Steve's newest message.</summary>
    public DateTimeOffset? LastOutboundAt { get; init; }

    /// <summary>The newest exchange's channel: where a follow-up goes.</summary>
    public Channel LastChannel { get; init; }

    /// <summary>The thread's subject, for the draft's Re: line.</summary>
    public string? LastSubject { get; init; }

    /// <summary>One clause of what was discussed, written by the 4 AM lane; no line means no draft.</summary>
    public string? TopicLine { get; init; }

    /// <summary>The file the TopicLine was read from.</summary>
    public string? TopicSource { get; init; }

    /// <summary>Where the row stands.</summary>
    public ConversationState State { get; init; }

    /// <summary>Why a Closed row closed: stale, bounce or declined.</summary>
    public string? CloseReason { get; init; }

    /// <summary>When the newest follow-up was drafted.</summary>
    public DateTimeOffset? DraftedAt { get; init; }

    /// <summary>The Gmail draft's id, or the LinkedIn queue file's name.</summary>
    public string? DraftId { get; init; }

    /// <summary>The channel the newest follow-up was written for.</summary>
    public Channel? DraftChannel { get; init; }

    /// <summary>How many follow-ups were sent.</summary>
    public int FollowUpCount { get; init; }

    /// <summary>What wrote the row: gmail, or the LinkedIn TSV's file name.</summary>
    public required string Source { get; init; }

    /// <summary>A tagged code for the optional blog line, null in this version.</summary>
    public string? RefCode { get; init; }

    /// <summary>The blog post the optional line names, null in this version.</summary>
    public string? BlogSlug { get; init; }

    /// <summary>The newer of the two last exchanges, or null when neither side has a time.</summary>
    public DateTimeOffset? LastExchangeAt => (LastInboundAt, LastOutboundAt) switch
    {
        ({ } a, { } b) => a > b ? a : b,
        ({ } a, null) => a,
        (null, { } b) => b,
        _ => null,
    };
}

/// <summary>One message of a thread as the network log reads it: who, when, which way, and the first words.</summary>
/// <param name="From">The From header as sent.</param>
/// <param name="At">When the message was sent, UTC.</param>
/// <param name="Outbound">True when Steve sent it (the SENT label or his address).</param>
/// <param name="HasCalendarPart">True when it carries a text/calendar part (an invite).</param>
/// <param name="Snippet">The message's opening text, which is the reply's own words above any quote.</param>
/// <param name="To">The To header as sent, empty when absent.</param>
/// <param name="RfcMessageId">The Message-Id header, for a reply's In-Reply-To.</param>
public sealed record ThreadMessage(string From, DateTimeOffset At, bool Outbound, bool HasCalendarPart, string Snippet = "", string To = "", string? RfcMessageId = null);

/// <summary>A thread's metadata: its id, subject and messages oldest first.</summary>
/// <param name="ThreadId">The Gmail thread id.</param>
/// <param name="Subject">The first message's subject.</param>
/// <param name="Messages">The messages, oldest first.</param>
public sealed record ThreadSummary(string ThreadId, string Subject, IReadOnlyList<ThreadMessage> Messages);
