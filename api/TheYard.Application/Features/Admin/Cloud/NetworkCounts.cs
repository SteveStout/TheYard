// The network log's counts as its card reads them (ADR-124): the card schema's
// Network view, which holds counts only. The port is here so the card's view is tested against a fake and the
// database is opened only in Infrastructure.
namespace TheYard.Application.Features.Admin.Cloud;

/// <summary>One read of the network view, or why there is none.</summary>
/// <param name="Read">True when the database answered.</param>
/// <param name="Note">Why it was not read; null when it was.</param>
/// <param name="Weeks">Conversations started, by the Monday of the ISO week, the last five weeks.</param>
/// <param name="States">Rows by state: Open, Drafted, Quiet, Stopped, Closed.</param>
public sealed record NetworkCountsRead(bool Read, string? Note, IReadOnlyList<(DateOnly Monday, int Started)> Weeks, IReadOnlyDictionary<string, int> States)
{
    /// <summary>A read that did not happen, with why.</summary>
    /// <param name="note">The sentence the card prints.</param>
    public static NetworkCountsRead Unread(string note) => new(false, note, [], new Dictionary<string, int>(StringComparer.Ordinal));
}

/// <summary>The network view in the card schema, read through the site's identity, which may read that schema and nothing else.</summary>
public interface INetworkCounts
{
    /// <summary>The weeks and the states, or why they could not be read.</summary>
    /// <param name="cancellation">Stops the read.</param>
    Task<NetworkCountsRead> ReadAsync(CancellationToken cancellation);
}
