namespace JobHunt.Application.Features.Jobs;

/// <summary>The pounce's own rows (jobhunt.PounceRoles) and the campaign's sends, as the tick reads them.</summary>
public interface IPounceReader
{
    /// <summary>True when jobhunt.PounceRoles exists (a schema not yet published answers false).</summary>
    Task<bool> ReadyAsync(CancellationToken ct);

    /// <summary>The passed roles still untouched whose live instant is at or after <paramref name="liveSince"/>.</summary>
    Task<IReadOnlyList<OpenRole>> OpenAsync(DateTimeOffset liveSince, CancellationToken ct);

    /// <summary>The roles that passed when judged at or after <paramref name="judgedSince"/>, with their posting's title.</summary>
    Task<IReadOnlyList<(string Company, string Title)>> PassedSinceAsync(DateTimeOffset judgedSince, CancellationToken ct);

    /// <summary>Every passed role whose live instant is at or after <paramref name="liveSince"/>, with its first touch or null.</summary>
    Task<IReadOnlyList<(DateTimeOffset Live, DateTimeOffset? Touch)>> CohortAsync(DateTimeOffset liveSince, CancellationToken ct);

    /// <summary>The campaign emails sent (Shadow 0) at or after <paramref name="since"/>, each with its lead's contact name.</summary>
    Task<IReadOnlyList<EmailTouch>> EmailsSinceAsync(DateTimeOffset since, CancellationToken ct);
}

/// <summary>The pounce's rows, read and written: one row a judged posting, and its first touch once.</summary>
public interface IPounceBook : IPounceReader
{
    /// <summary>Writes one row a judged posting; a posting that already has a row keeps it.</summary>
    Task AddAsync(IReadOnlyList<PounceRole> roles, CancellationToken ct);

    /// <summary>Sets a role's first touch, only while it has none, so a touch found is never moved later.</summary>
    Task MarkTouchedAsync(long key, DateTimeOffset at, string channel, CancellationToken ct);
}

/// <summary>A passed role still waiting for its first touch.</summary>
/// <param name="Key">The posting's key.</param>
/// <param name="Company">The company.</param>
/// <param name="LiveAt">When it went live.</param>
public sealed record OpenRole(long Key, string Company, DateTimeOffset LiveAt);

/// <summary>One judged posting, as jobhunt.PounceRoles holds it. No person's name is kept.</summary>
/// <param name="Key">jobhunt.Postings.Id.</param>
/// <param name="Company">The company.</param>
/// <param name="LiveAt">The board's instant, or the first sighting.</param>
/// <param name="LiveKnown">True when the board gave the instant.</param>
/// <param name="FirstSeenAt">When a scan first saw it.</param>
/// <param name="JudgedAt">When the tick judged it.</param>
/// <param name="Failed">The first reason it failed, or null when it passed.</param>
/// <param name="BandFlag">PASS, FLAG, SKIP or NONE.</param>
/// <param name="BudgetFlag">Went live in the last 21 days of a year not yet ended.</param>
public sealed record PounceRole(long Key, string Company, DateTimeOffset LiveAt, bool LiveKnown, DateTimeOffset FirstSeenAt, DateTimeOffset JudgedAt, string? Failed, string BandFlag, bool BudgetFlag)
{
    /// <summary>True when every gate passed it.</summary>
    public bool Passed => Failed is null;
}

/// <summary>What one tick did.</summary>
/// <param name="Fresh">Postings judged.</param>
/// <param name="Passed">The roles that passed, each with its title and the posting's address.</param>
/// <param name="Dropped">Postings that failed a gate.</param>
/// <param name="Touched">Roles whose first touch this tick found.</param>
/// <param name="Waiting">Passed roles inside the watch still untouched.</param>
/// <param name="Late">Passed roles first seen more than 30 minutes after going live.</param>
/// <param name="Days">The day lines, newest first, or empty on a tick that wrote none.</param>
public sealed record PounceTickResult(int Fresh, IReadOnlyList<(PounceRole Role, string Title, string Url)> Passed, int Dropped, int Touched, int Waiting, int Late, IReadOnlyList<PounceDay> Days);
