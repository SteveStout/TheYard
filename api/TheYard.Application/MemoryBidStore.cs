// The bid port held in this process's memory: the store a BidBook gets when nothing durable is
// wired, which is the unit tests and a backend whose store has not come up yet. It keeps the same
// compare-and-set rule the real stores keep, so a test that runs two services over one of these
// is a test of the rule and not of a shortcut.
namespace TheYard.Application;

/// <summary>
/// Bids and standings in a dictionary, gone when the process ends. One instance per BidBook
/// unless a test shares one on purpose: a shared instance is what two sites over one store look
/// like, in miniature.
/// </summary>
public sealed class MemoryBidStore : IBidStore
{
    /// <summary>Held around every read and write, so the compare and the set are one step.</summary>
    private readonly object _lock = new();

    /// <summary>One buyer's row per vehicle, keyed by the pair.</summary>
    private readonly Dictionary<(string User, string Vehicle), BidState> _bids = [];

    /// <summary>One standing per vehicle, and the version that moves on every write of it.</summary>
    private readonly Dictionary<string, (VehicleStanding Standing, long Version)> _standings = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<StoredBid>> LoadAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<StoredBid>>(
                _bids.Select(pair => new StoredBid(pair.Key.User, pair.Key.Vehicle, pair.Value)).ToList());
        }
    }

    public Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync()
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<StoredStanding>>(
                _standings.Select(pair => Stored(pair.Key, pair.Value)).ToList());
        }
    }

    public Task<StoredStanding?> ReadStandingAsync(string vehicleId)
    {
        lock (_lock)
        {
            return Task.FromResult(_standings.TryGetValue(vehicleId, out var held) ? Stored(vehicleId, held) : null);
        }
    }

    public Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<StoredBid>>(
                _bids.Where(pair => pair.Key.Vehicle == vehicleId)
                    .Select(pair => new StoredBid(pair.Key.User, pair.Key.Vehicle, pair.Value))
                    .ToList());
        }
    }

    public Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion)
    {
        lock (_lock)
        {
            // The compare: the version the caller decided against has to be the
            // one still here, or "nothing here" when it expected nothing.
            bool present = _standings.TryGetValue(vehicleId, out var held);
            string? current = present ? held.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            if (current != expectedVersion)
            {
                return Task.FromResult(false);
            }

            // The set: both writes or neither, which the lock makes true here
            // and a transaction makes true in the relational store.
            _bids[(userId, vehicleId)] = state;
            _standings[vehicleId] = (standing, present ? held.Version + 1 : 1);
            return Task.FromResult(true);
        }
    }

    public Task ClearAsync(string userId)
    {
        lock (_lock)
        {
            var mine = _bids.Keys.Where(key => key.User == userId).ToList();
            foreach (var key in mine)
            {
                _bids.Remove(key);
                _standings.Remove(key.Vehicle);
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>The stored shape of one entry, its version written the way every store hands one back: as text.</summary>
    private static StoredStanding Stored(string vehicleId, (VehicleStanding Standing, long Version) held) =>
        new(vehicleId, held.Standing, held.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
