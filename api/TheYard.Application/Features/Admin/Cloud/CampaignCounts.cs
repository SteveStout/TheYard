// The email campaign's counts as the campaign card reads them (ADR-118, Decision 6): the card schema of the
// campaign's database, which holds counts only. The port is here so the card's view is tested against a fake
// and the database is opened only in Infrastructure.
namespace TheYard.Application.Features.Admin.Cloud;

/// <summary>One Central day of the campaign: real sends, shadow would-sends, bounces and replies.</summary>
/// <param name="Day">The Central day.</param>
/// <param name="Sent">Emails sent.</param>
/// <param name="Shadow">Emails written in shadow and not sent.</param>
/// <param name="Bounced">Sends that bounced.</param>
/// <param name="Replies">Sends that drew a reply.</param>
public sealed record CampaignDay(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies);

/// <summary>Where the leads stand, counts only.</summary>
/// <param name="Open">Leads not blocked and not yet emailed.</param>
/// <param name="WithAddress">Of those, the ones with an address and the public page it came from.</param>
/// <param name="WaitingOnAddress">Leads with no public address yet.</param>
/// <param name="Blocked">Leads a rule blocked for the next lead run.</param>
/// <param name="NoMx">Addresses whose domain takes no mail.</param>
/// <param name="Unverified">Addresses a bounce marked.</param>
/// <param name="Companies">Companies in the book.</param>
/// <param name="Stopped">Companies that asked not to be written to.</param>
/// <param name="Boards">Companies with a public board the daily scan reads.</param>
/// <param name="LastScanAt">The daily scan's newest read.</param>
/// <param name="FirstWrittenAt">The first email written, shadow or sent: the card counts from it.</param>
/// <param name="FirstSentAt">The first email sent.</param>
/// <param name="LastBounceAt">The newest bounce.</param>
public sealed record CampaignLeadCounts(
    int Open, int WithAddress, int WaitingOnAddress, int Blocked, int NoMx, int Unverified, int Companies, int Stopped, int Boards,
    DateTimeOffset? LastScanAt, DateTimeOffset? FirstWrittenAt, DateTimeOffset? FirstSentAt, DateTimeOffset? LastBounceAt);

/// <summary>One cell of the Sunday table: the last four weeks of sends for one template, weekday, track and proof key.</summary>
/// <param name="Template">"B v1".</param>
/// <param name="Weekday">"Monday".</param>
/// <param name="Track">A or B.</param>
/// <param name="ProofKey">reliability, platform, leadership or ai.</param>
/// <param name="Sent">Emails sent.</param>
/// <param name="Replies">Replies they drew.</param>
public sealed record CampaignWeekRow(string Template, string Weekday, string Track, string ProofKey, int Sent, int Replies);

/// <summary>One read of the card schema, or why there is none.</summary>
/// <param name="Read">True when the database answered.</param>
/// <param name="Note">Why it was not read; null when it was.</param>
/// <param name="Days">The last sixty Central days that carry an email.</param>
/// <param name="Leads">Where the leads stand; null when not read.</param>
/// <param name="Weeks">The Sunday table's cells.</param>
public sealed record CampaignCountsRead(bool Read, string? Note, IReadOnlyList<CampaignDay> Days, CampaignLeadCounts? Leads, IReadOnlyList<CampaignWeekRow> Weeks)
{
    /// <summary>A read that did not happen, with why.</summary>
    /// <param name="note">The sentence the card prints.</param>
    public static CampaignCountsRead Unread(string note) => new(false, note, [], null, []);
}

/// <summary>The campaign database's card schema, read through the site's identity, which may read that schema and nothing else.</summary>
public interface ICampaignCounts
{
    /// <summary>The days, the leads and the Sunday table, or why they could not be read.</summary>
    /// <param name="cancellation">Stops the read.</param>
    Task<CampaignCountsRead> ReadAsync(CancellationToken cancellation);
}
