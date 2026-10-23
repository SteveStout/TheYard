using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// Two sites run one image over one store, so two BidBook instances over one
/// store are the shape production has. These hold the rule that each vehicle's
/// standing belongs to the store: the second instance cannot sell a vehicle the
/// first sold, and cannot accept a bid under the first's standing, although
/// neither instance's memory has ever seen the other's bid. Once over the
/// in-memory store, once over a real SQLite file through the relational
/// adapter, so the compare-and-set is the database's and not a lock in a test
/// (ADR: Three readers with no memory of the project, item 5).
/// </summary>
public sealed class StandingAcrossSitesTests : IDisposable
{
    private static readonly AuctionClock Now =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-sites-{Guid.NewGuid():N}.db");

    private YardConnection Connection => new(YardProvider.Sqlite, $"Data Source={_file};Pooling=False");

    /// <summary>The two stores under test: the in-memory one and the relational one over a scratch file.</summary>
    public static TheoryData<string> Stores => ["memory", "sqlite"];

    /// <summary>One store, and two services over it standing in for the two sites.</summary>
    private (BidBook First, BidBook Second) TwoSites(string kind)
    {
        IBidStore store;
        if (kind == "memory")
        {
            store = new MemoryBidStore();
        }
        else
        {
            using var db = new YardDbContext(Connection.Options());
            db.Database.Migrate();
            foreach (string id in new[] { "buyer-a", "buyer-b" })
            {
                db.Users.Add(new YardUser
                {
                    Id = id,
                    UserName = $"{id}@example.com",
                    NormalizedUserName = $"{id.ToUpperInvariant()}@EXAMPLE.COM",
                    Email = $"{id}@example.com",
                    NormalizedEmail = $"{id.ToUpperInvariant()}@EXAMPLE.COM",
                    CreatedAtMs = 1,
                });
            }
            db.SaveChanges();
            store = new EfBidStore(new PlainFactory(Connection));
        }
        return (new BidBook(store), new BidBook(store));
    }

    private static Vehicle Live(int? currentBid = 22_800)
    {
        for (int i = 0; i < 1000; i++)
        {
            if (AuctionSchedule.StatusFor($"site-{i}", Now) == AuctionStatus.Live)
            {
                return TestData.Vehicle(id: $"site-{i}", currentBid: currentBid) with { BuyNowPrice = 28_000 };
            }
        }
        throw new InvalidOperationException("no live id found");
    }

    // #region across-sites
    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_vehicle_one_site_sold_cannot_be_sold_again_on_the_other(string kind)
    {
        var (first, second) = TwoSites(kind);
        var vehicle = Live();
        await first.LoadAsync();
        await second.LoadAsync();

        Assert.Equal(BidOutcomeKind.Won, (await first.BuyNowAsync(vehicle, Now, "buyer-a")).Kind);

        // The second site's memory has never seen that sale; the store has.
        Assert.False(second.IsSold(vehicle.Id));
        var again = await second.BuyNowAsync(vehicle, Now, "buyer-b");
        var bid = await second.PlaceBidAsync(vehicle, 30_000, Now, "buyer-b");

        Assert.Equal(BidOutcomeKind.Rejected, again.Kind);
        Assert.Equal(BidRules.SoldReason, again.Reason);
        Assert.Equal(BidRules.SoldReason, bid.Reason);
        // And reading the store taught the second site the sale.
        Assert.True(second.IsSold(vehicle.Id));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_bid_under_the_other_sites_standing_is_refused(string kind)
    {
        var (first, second) = TwoSites(kind);
        var vehicle = Live(22_800);
        await first.LoadAsync();
        await second.LoadAsync();

        Assert.Equal(BidOutcomeKind.Accepted, (await first.PlaceBidAsync(vehicle, 24_000, Now, "buyer-a")).Kind);

        // 23,300 cleared the dataset's minimum and is under the first site's bid.
        var under = await second.PlaceBidAsync(vehicle, 23_300, Now, "buyer-b");
        Assert.Equal(BidOutcomeKind.Rejected, under.Kind);
        Assert.Contains("24,500", under.Reason);

        var over = await second.PlaceBidAsync(vehicle, 24_500, Now, "buyer-b");
        Assert.Equal(BidOutcomeKind.Accepted, over.Kind);
        Assert.Equal("buyer-b", second.Standing()[vehicle.Id].HighBidderId);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_restart_rebuilds_the_cache_from_the_store(string kind)
    {
        var (first, second) = TwoSites(kind);
        var vehicle = Live(22_800);
        await first.PlaceBidAsync(vehicle, 24_000, Now, "buyer-a");
        await second.PlaceBidAsync(vehicle, 24_500, Now, "buyer-b");

        await first.RefreshAsync();

        Assert.Equal(24_500, first.Apply(vehicle).CurrentBid);
        Assert.Equal("buyer-b", first.Standing()[vehicle.Id].HighBidderId);
        Assert.Equal(24_000, first.SnapshotFor("buyer-a")[vehicle.Id].Amount);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_reset_on_one_site_keeps_the_bid_placed_on_the_other(string kind)
    {
        var (first, second) = TwoSites(kind);
        var vehicle = Live(22_800);
        await first.PlaceBidAsync(vehicle, 24_000, Now, "buyer-a");
        await second.PlaceBidAsync(vehicle, 24_500, Now, "buyer-b");
        await first.PlaceBidAsync(vehicle, 25_000, Now, "buyer-a");

        // The first site never saw buyer-b's bid in memory; the reset reads the rows.
        Assert.Empty(await first.ResetAsync("buyer-a"));
        Assert.Equal(24_500, first.Apply(vehicle).CurrentBid);

        var under = await second.PlaceBidAsync(vehicle, 24_600, Now, "buyer-b");
        Assert.Equal(BidOutcomeKind.Rejected, under.Kind);
    }
    // #endregion across-sites

    /// <summary>The smallest thing that satisfies IDbContextFactory, so a store can be built without a service provider.</summary>
    private sealed class PlainFactory(YardConnection connection) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => new(connection.Options());
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}
