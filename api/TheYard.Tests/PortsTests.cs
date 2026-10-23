using System.Reflection;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

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
            service.PlaceBidAsync(vehicle, minimum, clock, $"buyer-{i}")));

        int accepted = outcomes.Count(o => o.Kind == BidOutcomeKind.Accepted);
        Assert.Equal(1, accepted);
        Assert.Equal(1, store.Writes);
        Assert.Equal(49, outcomes.Count(o => o.Kind == BidOutcomeKind.Rejected));
    }

    private sealed class SlowStore : IBidStore
    {
        private readonly MemoryBidStore _inner = new();

        public int Writes;

        public Task<IReadOnlyList<StoredBid>> LoadAsync() => _inner.LoadAsync();

        public Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync() => _inner.LoadStandingsAsync();

        public Task<StoredStanding?> ReadStandingAsync(string vehicleId) => _inner.ReadStandingAsync(vehicleId);

        public Task<IReadOnlyList<StoredBid>> BidsOnAsync(string vehicleId) => _inner.BidsOnAsync(vehicleId);

        public async Task<bool> TrySaveAsync(string userId, string vehicleId, BidState state, VehicleStanding standing, string? expectedVersion)
        {
            await Task.Delay(5);
            bool saved = await _inner.TrySaveAsync(userId, vehicleId, state, standing, expectedVersion);
            if (saved)
            {
                Interlocked.Increment(ref Writes);
            }
            return saved;
        }

        public Task ClearAsync(string userId) => _inner.ClearAsync(userId);
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
