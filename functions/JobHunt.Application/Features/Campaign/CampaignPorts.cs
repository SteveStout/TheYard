namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The companies the campaign knows (ADR-118, Decision 2): written by the lead run, read by the send rules,
/// and marked do-not-email by the inbox pass when a reply asks to stop.
/// </summary>
public interface ICompanyBook
{
    /// <summary>One company by its key, or null.</summary>
    Task<CompanyRecord?> FindAsync(string key, CancellationToken ct);

    /// <summary>A reply asked to stop: no email to this company again.</summary>
    Task MarkDoNotEmailAsync(int companyId, DateTimeOffset at, CancellationToken ct);

    /// <summary>Writes a company from the lead run, new or changed, with its evidence rows; answers its id. Never clears DoNotEmail.</summary>
    Task<int> SaveAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct);

    /// <summary>Every company with a public board to scan.</summary>
    Task<IReadOnlyList<BoardCompany>> BoardsAsync(CancellationToken ct);

    /// <summary>The daily scan's answer for one company: the newest .NET posting it saw, and when it looked.</summary>
    Task MarkScannedAsync(int companyId, DateOnly? lastDotNetPostingAt, string? lastDotNetPostingTitle, DateTimeOffset at, CancellationToken ct);
}

/// <summary>A company with its public board.</summary>
public sealed record BoardCompany(int Id, string Kind, string Token)
{
    /// <summary>The company's key (the registrable domain), carried onto the postings its board answers.</summary>
    public string? Key { get; init; }
}

/// <summary>A domain's MX records, asked of a public resolver: true found, false none, null not answered.</summary>
public interface IMxCheck
{
    /// <summary>Whether the domain takes mail.</summary>
    Task<bool?> HasMxAsync(string domain, CancellationToken ct);
}

/// <summary>One posting on a company's public board, as the scan reads it.</summary>
public sealed record BoardPosting(string Title, string Text, DateOnly? Posted)
{
    /// <summary>The board's own id for the posting.</summary>
    public string Id { get; init; } = "";

    /// <summary>The posting's public address.</summary>
    public string Url { get; init; } = "";

    /// <summary>The location as the board writes it.</summary>
    public string Location { get; init; } = "";

    /// <summary>The board's workplace field (remote, hybrid, onsite), when it has one.</summary>
    public string? Workplace { get; init; }

    /// <summary>The board's remote flag, when it has one.</summary>
    public bool? IsRemote { get; init; }

    /// <summary>The published band's low end a year, or null when the board published none.</summary>
    public int? CompMin { get; init; }

    /// <summary>The published band's high end a year, or null.</summary>
    public int? CompMax { get; init; }

    /// <summary>The band's currency.</summary>
    public string? CompCurrency { get; init; }

    /// <summary>The band's interval as the board wrote it.</summary>
    public string? CompInterval { get; init; }
}

/// <summary>
/// The postings the board scan keeps (jobhunt.Postings and its three tables): one row a posting keyed by its board
/// and its board's id, a change row for every compared field that moved, a description on first sight and on a
/// changed hash only. The Function is the only writer; nothing here deletes.
/// </summary>
public interface IPostingBook
{
    /// <summary>True when the postings tables exist in the database (a schema not yet published answers false).</summary>
    Task<bool> ReadyAsync(CancellationToken ct);

    /// <summary>How many posting rows there are, so the first pass can seed once.</summary>
    Task<int> CountAsync(CancellationToken ct);

    /// <summary>The board's rows that are open, or closed since <paramref name="closedSince"/>.</summary>
    Task<IReadOnlyList<StoredPosting>> BoardAsync(string source, string token, DateTimeOffset closedSince, CancellationToken ct);

    /// <summary>A posting seen for the first time; answers its key.</summary>
    Task<long> AddAsync(Posting posting, string? companyKey, DateTimeOffset seenAt, CancellationToken ct);

    /// <summary>A posting seen again with something changed: every field written, LastSeenAt moved, ClosedAt cleared.</summary>
    Task UpdateAsync(long key, Posting posting, DateTimeOffset at, CancellationToken ct);

    /// <summary>Postings seen again unchanged: LastSeenAt moved, ClosedAt cleared.</summary>
    Task SeenAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct);

    /// <summary>Postings a scan no longer sees: ClosedAt set.</summary>
    Task CloseAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct);

    /// <summary>One changed field, or a re-post link (Field repost_of).</summary>
    Task ChangeAsync(long key, string field, string? oldValue, string? newValue, DateTimeOffset at, CancellationToken ct);

    /// <summary>A description, on first sight or a new hash.</summary>
    Task DescribeAsync(long key, string hash, string text, DateTimeOffset at, CancellationToken ct);
}

/// <summary>The watchlist: the companies with a board the daily scan reads, grown from the boards inbox.</summary>
public interface IWatchlist
{
    /// <summary>How many companies have a board.</summary>
    Task<int> BoardCountAsync(CancellationToken ct);

    /// <summary>Adds the board to its company (made when new, given the board when it had none); false when the board is already on a company or the company has another.</summary>
    Task<bool> AddBoardAsync(BoardRow row, string run, DateTimeOffset at, CancellationToken ct);
}

/// <summary>A company's public job board: Greenhouse, Lever or Ashby, keyless JSON, one request a company.</summary>
public interface IBoardReader
{
    /// <summary>The board's open postings, or null when the board did not answer.</summary>
    Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct);
}

/// <summary>The leads the model wrote, as the send rules read them.</summary>
public interface ILeadStore
{
    /// <summary>Every lead not blocked, with its company, oldest first.</summary>
    Task<IReadOnlyList<LeadRecord>> OpenAsync(CancellationToken ct);

    /// <summary>A rule held the lead: its reason is kept on the row for the next lead run to read.</summary>
    Task BlockAsync(long leadId, string reason, CancellationToken ct);

    /// <summary>A bounce or a check said the address does not take mail.</summary>
    Task MarkUnverifiedAsync(long leadId, string reason, CancellationToken ct);

    /// <summary>
    /// Writes a lead from the lead run, matched on its company and its manager's name: new, or changed where the
    /// run changed it. The MX answer is the importer's; Strength and its hash are computed by code, not the run.
    /// </summary>
    Task SaveAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string? blocked, string run, DateTimeOffset at, CancellationToken ct);
}

/// <summary>Every campaign email, sent or written in shadow.</summary>
public interface IEmailLog
{
    /// <summary>The sends from a Central day on, real and shadow, for the 30-day rule.</summary>
    Task<IReadOnlyList<LoggedEmail>> SinceAsync(DateOnly centralDay, CancellationToken ct);

    /// <summary>
    /// The Central days that carry at least one real send, each once. The ramp counts the ones before today to
    /// know which day of `on` this is; asking for every row since the year 1 instead fails in SQL, whose oldest
    /// datetime is in 1753.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> OnDaysAsync(CancellationToken ct);

    /// <summary>How many emails a Central day already carries, of one kind: the cap reads this before every send.</summary>
    Task<int> CountAsync(DateOnly centralDay, bool shadow, CancellationToken ct);

    /// <summary>Writes one email's row and answers its id.</summary>
    Task<long> WriteAsync(EmailRecord email, CancellationToken ct);

    /// <summary>The real sends from a Central day on that carry a Gmail thread: the bounce and reply joins read these.</summary>
    Task<IReadOnlyList<SentThread>> SentThreadsAsync(DateOnly centralDay, CancellationToken ct);

    /// <summary>A delivery failure came back for this email: it bounced, with the reason.</summary>
    Task MarkBouncedAsync(long emailId, string reason, CancellationToken ct);

    /// <summary>A reply arrived in a sent thread.</summary>
    Task MarkReplyAsync(string gmailThreadId, DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// Where an address comes from when no public source published one. No third-party finder is used, so the one
/// adapter is NoFinder, which answers nothing. The port exists anyway, and that is the decision worth reading:
/// a finder is a question to settle on the shadow week's own numbers rather than on a guess, and keeping the
/// seam means settling it costs one adapter instead of a change to every caller.
/// </summary>
public interface IAddressFinder
{
    /// <summary>The finder's name for the card and the pass log.</summary>
    string Name { get; }

    /// <summary>An address with its source, or null with nothing spent.</summary>
    Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct);
}

/// <summary>An address and the URL it was read from.</summary>
public sealed record FoundAddress(string Address, string SourceUrl);

/// <summary>One company row.</summary>
public sealed record CompanyRecord(
    int Id,
    string Key,
    string? ParentKey,
    IReadOnlyList<string> Names,
    string RemoteStance,
    DateOnly? LastDotNetPostingAt,
    string? LastDotNetPostingTitle,
    int DotNetEvidence,
    bool DoNotEmail);

/// <summary>One lead row with the company facts its rules read.</summary>
public sealed record LeadRecord
{
    public required long Id { get; init; }
    public required CompanyRecord Company { get; init; }
    public required char Track { get; init; }
    public required string CompanyName { get; init; }
    public string? RoleTitle { get; init; }
    public required string WhatTheyDo { get; init; }
    public required string FirstName { get; init; }
    public required string ContactName { get; init; }
    public required string ContactTitle { get; init; }
    public string? ContactEmail { get; init; }
    public string? EmailSource { get; init; }
    public bool? MxChecked { get; init; }
    public bool? Verified { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public DateTimeOffset? LinkedInInviteSentAt { get; init; }
    public DateTimeOffset? LinkedInAcceptedAt { get; init; }
    public string? ProofKey { get; init; }
    public string? FollowUpPostSlug { get; init; }
    public int? GhostScore { get; init; }
    public int? BandTop { get; init; }
    public required DateOnly FirstSeen { get; init; }
    public int StrengthStep { get; init; }
    public string? RefCode { get; init; }
    public string? ResumeLink { get; init; }
    public string? SiteLink { get; init; }
    public string? GitHubLink { get; init; }
    public string? LinkedInLink { get; init; }
    public string? AboutLink { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One logged email as the 30-day rule reads it.</summary>
public sealed record LoggedEmail(long LeadId, string Key, string? ParentKey, string Company, DateOnly CentralDay, bool Shadow);

/// <summary>One real send with its thread, as the inbox side reads it.</summary>
public sealed record SentThread(long EmailId, long LeadId, string Key, string To, string ThreadId, DateOnly CentralDay, bool Bounced, DateTimeOffset? ReplyReceivedAt);

/// <summary>One email to write: what went out, whole, so it can be rebuilt without the template.</summary>
public sealed record EmailRecord
{
    public required long LeadId { get; init; }
    public required string Key { get; init; }
    public string? ParentKey { get; init; }
    public required string Company { get; init; }
    public required string To { get; init; }
    public required string TemplateId { get; init; }
    public required string TemplateVersion { get; init; }
    public required string Subject { get; init; }
    public required string BodyText { get; init; }
    public required string BodyHtml { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public required DateOnly CentralDay { get; init; }
    public required int Touch { get; init; }
    public required bool Shadow { get; init; }
    public string? RefCode { get; init; }
    public string? GmailMessageId { get; init; }
    public string? GmailThreadId { get; init; }
    public string? RfcMessageId { get; init; }
}

/// <summary>What the Track A feed reads and writes in the campaign's tables.</summary>
public interface ITrackABook
{
    /// <summary>The keys of the companies with a Track A lead applied to since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<string>> TrackAKeysAsync(DateTimeOffset since, CancellationToken ct);

    /// <summary>The company's id, the company written first when the book does not hold it.</summary>
    Task<int> CompanyIdAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct);

    /// <summary>Writes a lead, as the lead import does.</summary>
    Task SaveLeadAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string run, DateTimeOffset at, CancellationToken ct);
}
