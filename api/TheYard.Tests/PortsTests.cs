using System.Reflection;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The shape of the seams, held by a test because the shape is the
/// decision (ADR: The ports learn to wait).
/// </summary>
public class PortsTests
{
    // #region port-surface
    /// <summary>
    /// Every member of every port returns a Task. The first store was a file and
    /// the ports were synchronous because reading a file is; the third store has
    /// no synchronous driver at all, and a port that blocks a request thread while
    /// a cloud store answers is a performance defect on a one-vCPU container. A
    /// synchronous member added later fails here rather than in production.
    /// </summary>
    [Theory]
    [InlineData(typeof(IVehicleSource))]
    [InlineData(typeof(IPhotoManifestSource))]
    [InlineData(typeof(IBidStore))]
    [InlineData(typeof(IVehicleCatalogue))]
    public void Every_port_member_returns_a_task(Type port)
    {
        // A property is a fact about the adapter fixed when it was built (the catalogue's size and
        // how it wants to be filled), never a read of the store, so only the methods are held.
        var methods = port.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => !method.IsSpecialName).ToArray();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.True(
                typeof(Task).IsAssignableFrom(method.ReturnType),
                $"{port.Name}.{method.Name} returns {method.ReturnType.Name}, and every port member has to be awaitable");
        }
    }
    // #endregion port-surface

    // #region port-cancellation
    /// <summary>
    /// Every member of every port takes a CancellationToken. The ports learned to
    /// wait before they learned to stop: the record that made them awaitable left the
    /// token out on purpose, and the cost of that showed up as work the site paid for
    /// after the caller had gone (ADR: The ports learn to wait, the addendum on the
    /// token). A read a visitor abandoned keeps a query running against the store, and
    /// a path that drops the token cannot be made to stop later without changing every
    /// caller, which is the reason to hold the shape here rather than the behaviour.
    /// A member added later with no token fails on this line.
    /// </summary>
    [Theory]
    [InlineData(typeof(IVehicleSource))]
    [InlineData(typeof(IPhotoManifestSource))]
    [InlineData(typeof(IBidStore))]
    [InlineData(typeof(IVehicleCatalogue))]
    public void Every_port_member_takes_a_cancellation_token(Type port)
    {
        // Properties are facts the adapter was built with, never a read of the store,
        // so the surface test's filter is the one used here too.
        var methods = port.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => !method.IsSpecialName).ToArray();
        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.True(
                method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)),
                $"{port.Name}.{method.Name} takes no CancellationToken, and a caller who has gone has to be able to stop the store's work");
        }
    }
    // #endregion port-cancellation

    // #region token-reaches-the-store
    /// <summary>
    /// The token a caller hands the use case is the token the store is asked with.
    /// Declaring the parameter is the shape; carrying the caller's own token the
    /// whole way down is the behaviour, and a version that threads the parameter
    /// and then passes <c>CancellationToken.None</c> at the last call satisfies the
    /// shape test and nothing else. So the catalogue records what it was given and
    /// the test cancels its own source afterwards: the token the store held moves
    /// with it, which only one token can do.
    /// </summary>
    [Fact]
    public async Task A_read_hands_the_callers_own_token_all_the_way_to_the_store()
    {
        var seeds = new[] { TestData.Vehicle(id: "token-1") };
        var store = new TokenRecordingCatalogue(seeds);
        var service = new Stock(store, new SeedVehicles(seeds), new NoPhotos(), CatalogueUpkeep.Off);
        using var caller = new CancellationTokenSource();
        await service.WarmAsync(caller.Token);

        await service.FindAsync("token-1", caller.Token);

        Assert.True(store.Seen.CanBeCanceled, "the store was asked with a token nobody can cancel");
        Assert.False(store.Seen.IsCancellationRequested);
        await caller.CancelAsync();
        Assert.True(store.Seen.IsCancellationRequested, "cancelling the caller left the store's token alone, so the token it got was not the caller's");
    }

    /// <summary>A catalogue that answers from memory and keeps the token its last read was given.</summary>
    private sealed class TokenRecordingCatalogue(IReadOnlyList<Vehicle> held) : IVehicleCatalogue
    {
        private readonly MemoryCatalogue _inner = new(held);

        public CancellationToken Seen { get; private set; }

        public int Target => held.Count;

        public CatalogueFillPace Pace => new(held.Count, TimeSpan.Zero, held.Count);

        public Task<int> FilledAsync(CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.FilledAsync(cancellation);
        }

        public Task FillAsync(IReadOnlyList<CatalogueEntry> batch, CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.FillAsync(batch, cancellation);
        }

        public Task<SegmentCounts> CountAsync(CatalogueFilter filter, CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.CountAsync(filter, cancellation);
        }

        public Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take, CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.PageAsync(filter, order, skip, take, cancellation);
        }

        public Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids, CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.FindAsync(ids, cancellation);
        }

        public Task<InventoryFacets> FacetsAsync(CancellationToken cancellation)
        {
            Seen = cancellation;
            return _inner.FacetsAsync(cancellation);
        }
    }

    private sealed class SeedVehicles(IReadOnlyList<Vehicle> held) : IVehicleSource
    {
        public Task<IReadOnlyList<Vehicle>> LoadAsync(CancellationToken cancellation) => Task.FromResult(held);
    }

    private sealed class NoPhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
    // #endregion token-reaches-the-store

    // #region gate
    /// <summary>
    /// The semaphore that replaced the lock still admits one bidder at a time.
    /// Fifty bids at the minimum arrive together on one vehicle through a store
    /// that takes a moment to answer; with the gate, exactly one of them is
    /// accepted at each price and the rest are refused as too low, so the count
    /// of accepted bids equals the count of distinct amounts that landed. Without
    /// it, two bidders read the same standing price, both pass the rules, and the
    /// lower one lands second.
    /// </summary>
    [Fact]
    public async Task The_gate_admits_one_bidder_at_a_time_while_the_store_is_answering()
    {
        var clock = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var vehicle = LiveVehicleFor(clock);
        var store = new SlowStore();
        var service = new BidBook(store);
        int minimum = BidRules.MinNextBid(vehicle);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
            service.PlaceBidAsync(vehicle, minimum, clock, $"buyer-{i}", CancellationToken.None)));

        int accepted = outcomes.Count(o => o.Kind == BidOutcomeKind.Accepted);
        Assert.Equal(1, accepted);
        Assert.Equal(1, store.Writes);
        Assert.Equal(49, outcomes.Count(o => o.Kind == BidOutcomeKind.Rejected));
    }

    private sealed class SlowStore : IBidStore
    {
        private readonly MemoryBidStore _inner = new();

        public int Writes;

        public Task<IReadOnlyList<StoredBid>> LoadAsync(CancellationToken cancellation) => _inner.LoadAsync(CancellationToken.None);

        public Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync(CancellationToken cancellation) => _inner.LoadStandingsAsync(CancellationToken.None);

        public Task<StoredStanding?> ReadStandingAsync(string vehicleId, CancellationToken cancellation) => _inner.ReadStandingAsync(vehicleId, CancellationToken.None);

        public Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId, CancellationToken cancellation) => _inner.BidsOnAsync(vehicleId, CancellationToken.None);

        public async Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion, CancellationToken cancellation)
        {
            await Task.Delay(5);
            bool saved = await _inner.TrySaveAsync(userId, vehicleId, state, standing, expectedVersion, CancellationToken.None);
            if (saved)
            {
                Interlocked.Increment(ref Writes);
            }
            return saved;
        }

        public Task ClearAsync(string userId, CancellationToken cancellation) => _inner.ClearAsync(userId, CancellationToken.None);
    }
    // #endregion gate

    private static Vehicle LiveVehicleFor(AuctionClock clock)
    {
        for (int i = 0; i < 500; i++)
        {
            string candidate = $"gate-{i}";
            if (AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live)
            {
                return TestData.Vehicle(id: candidate, currentBid: 22_800);
            }
        }
        throw new InvalidOperationException("no live id found, which the schedule makes impossible");
    }
}
