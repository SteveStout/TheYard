namespace JobHunt.Application.Features.Jobs;

/// <summary>The postings the board scan saw open tonight, as the picker reads them; it never writes them.</summary>
public interface IJobsPostings
{
    /// <summary>True when the postings tables exist (a schema not yet published answers false).</summary>
    Task<bool> ReadyAsync(CancellationToken ct);

    /// <summary>Every open posting the scan saw since <paramref name="seenSince"/>, keyed, with no description text.</summary>
    Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct);

    /// <summary>The newest description text of each posting asked for.</summary>
    Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct);

    /// <summary>The postings first seen since <paramref name="seenSince"/> that the pounce has not judged, with no description text.</summary>
    Task<IReadOnlyList<FreshPosting>> FreshAsync(DateTimeOffset seenSince, CancellationToken ct);
}

/// <summary>A posting the pounce has not judged yet.</summary>
/// <param name="Key">jobhunt.Postings.Id.</param>
/// <param name="Candidate">The role, with no text.</param>
/// <param name="FirstSeenAt">When a scan first saw it.</param>
/// <param name="PublishedAt">The instant its board says it went up, or null.</param>
/// <param name="CompanyKey">The campaign's key for its company, the registrable domain, when the scan knows it.</param>
public sealed record FreshPosting(long Key, Candidate Candidate, DateTimeOffset FirstSeenAt, DateTimeOffset? PublishedAt, string? CompanyKey = null);

/// <summary>The tracker's rows as the SQL copy holds them (jobhunt.Tracker), seven cells each, in file order.</summary>
public interface ITrackerRows
{
    /// <summary>Every row under the header.</summary>
    Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct);

    /// <summary>The rows written to SQL at or after <paramref name="since"/>, each with its instant.</summary>
    Task<IReadOnlyList<TrackerTouch>> SinceAsync(DateTimeOffset since, CancellationToken ct);
}

/// <summary>What one picker pass found.</summary>
/// <param name="Line">The pass's line, counts only.</param>
/// <param name="Path">The candidates file it wrote.</param>
/// <param name="Scanned">Open postings the scan saw tonight.</param>
/// <param name="Boards">Boards they came from.</param>
/// <param name="Alerts">Alert leads read.</param>
/// <param name="Passed">Candidates every gate passed.</param>
/// <param name="Confirmed">Of them, read from the employer's own board tonight.</param>
public sealed record JobsPickResult(string Line, string Path, int Scanned, int Boards, int Alerts, int Passed, int Confirmed);
