namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// LinkedIn before email (the review, 8.2 item 3): a lead whose manager accepted Steve's invite goes first, then
/// one whose invite has been out more than three days, then the rest (no invite, or one sent in the last three
/// days, which gets its three days before an email follows it). Still one email a company in 30 days.
/// </summary>
public static class LinkedInOrder
{
    /// <summary>The days an invite is given before an email follows it.</summary>
    public const int InviteDays = 3;

    /// <summary>0 accepted, 1 invite out more than three days, 2 the rest; lower sends first.</summary>
    public static int Rank(DateTimeOffset? acceptedAt, DateTimeOffset? inviteSentAt, DateTimeOffset now) =>
        acceptedAt is not null ? 0
        : inviteSentAt is { } sent && now - sent > TimeSpan.FromDays(InviteDays) ? 1
        : 2;
}

/// <summary>One lead as the send order reads it.</summary>
public sealed record RankedLead(long LeadId, int LinkedInRank, int Strength, DateTimeOffset CreatedAt)
{
    /// <summary>A or B.</summary>
    public char Track { get; init; } = 'B';

    /// <summary>When Steve applied, for a Track A lead.</summary>
    public DateTimeOffset? AppliedAt { get; init; }
}

/// <summary>
/// The order the day's sends go in, after every hold: Track A first, the oldest application first, because the
/// email after an application is worth most while the application is fresh and its seven days run out; then the
/// LinkedIn rank, then Strength high to low, then the oldest lead first (F9: ties break on CreatedAt), then the
/// lead's id so the order is total.
/// </summary>
public static class SendOrder
{
    /// <summary>The leads in send order.</summary>
    public static IReadOnlyList<RankedLead> Sort(IEnumerable<RankedLead> leads) =>
        [.. leads.OrderBy(l => l.Track == 'A' ? 0 : 1).ThenBy(l => l.Track == 'A' ? l.AppliedAt ?? DateTimeOffset.MaxValue : DateTimeOffset.MinValue)
            .ThenBy(l => l.LinkedInRank).ThenByDescending(l => l.Strength).ThenBy(l => l.CreatedAt).ThenBy(l => l.LeadId)];
}
