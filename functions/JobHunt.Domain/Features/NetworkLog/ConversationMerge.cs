namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// How one read of a thread moves a conversation's row: a new row starts Open at its first exchange; a stored row
/// keeps its earliest start, moves its last exchanges forward and never back, and puts the thread first in its list.
/// A person writing again opens a Quiet, stale or declined row; a row that asked to stop, or bounced, stays shut.
/// </summary>
public static class ConversationMerge
{
    /// <summary>The close reasons a new message from the person lifts.</summary>
    public static readonly IReadOnlySet<string> Reopenable = new HashSet<string>(StringComparer.Ordinal) { "stale", "declined" };

    /// <summary>A new email row from a two-way thread.</summary>
    public static Conversation NewEmail(TwoWayThread read, ThreadSummary thread)
    {
        string full = Mail.Name(read.From);
        string token = MatchKey.TokenOfAddress(read.Address);
        string key = CompanyKey.Of(read.Address);
        bool personal = key.Length == 0 || PersonalDomains.All.Contains(key);
        return new Conversation
        {
            PersonKey = PersonKey.ForEmail(read.Address),
            MatchKey = MatchKey.Of(full, token),
            FirstName = MatchKey.FirstName(full),
            FullName = full.Length > 0 ? full : read.Address,
            Company = personal ? null : token,
            CompanyKey = personal ? null : key,
            Channel = Channel.Email,
            Email = read.Address,
            ThreadIds = [thread.ThreadId],
            FirstTwoWayAt = read.FirstTwoWayAt,
            LastInboundAt = read.LastInboundAt,
            LastOutboundAt = read.LastOutboundAt,
            LastChannel = Channel.Email,
            LastSubject = thread.Subject,
            State = ConversationState.Open,
            Source = "gmail",
        };
    }

    /// <summary>The stored row moved by a newer read of one of its conversations, on either channel.</summary>
    public static Conversation Merge(Conversation stored, Conversation read)
    {
        var lastIn = Later(stored.LastInboundAt, read.LastInboundAt);
        var lastOut = Later(stored.LastOutboundAt, read.LastOutboundAt);
        bool newer = read.LastExchangeAt is { } r && (stored.LastExchangeAt is not { } s || r > s);
        bool spoke = read.LastInboundAt is { } i && (stored.LastInboundAt is not { } si || i > si);
        var state = stored.State;
        string? reason = stored.CloseReason;
        if (spoke && (state == ConversationState.Quiet || (state == ConversationState.Closed && reason is not null && Reopenable.Contains(reason))))
        {
            state = ConversationState.Open;
            reason = null;
        }
        return stored with
        {
            MatchKey = stored.MatchKey ?? read.MatchKey,
            FirstName = stored.FirstName.Length > 0 ? stored.FirstName : read.FirstName,
            Company = stored.Company ?? read.Company,
            CompanyKey = stored.CompanyKey ?? read.CompanyKey,
            Email = stored.Email ?? read.Email,
            ProfileUrl = stored.ProfileUrl ?? read.ProfileUrl,
            ThreadIds = [.. read.ThreadIds.Concat(stored.ThreadIds).Distinct(StringComparer.Ordinal).Take(20)],
            FirstTwoWayAt = read.FirstTwoWayAt < stored.FirstTwoWayAt ? read.FirstTwoWayAt : stored.FirstTwoWayAt,
            LastInboundAt = lastIn,
            LastOutboundAt = lastOut,
            LastChannel = newer ? read.LastChannel : stored.LastChannel,
            LastSubject = newer && read.LastSubject is not null ? read.LastSubject : stored.LastSubject,
            State = state,
            CloseReason = reason,
        };
    }

    /// <summary>The row with a stop verdict applied: Stopped or Closed, unless it already is.</summary>
    public static Conversation Stop(Conversation row, StopVerdict verdict) =>
        row.State is ConversationState.Stopped || (row.State == ConversationState.Closed && row.CloseReason == "bounce")
            ? row
            : row with { State = verdict.State, CloseReason = verdict.Reason };

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => (a, b) switch
    {
        ({ } x, { } y) => x > y ? x : y,
        _ => a ?? b,
    };
}
