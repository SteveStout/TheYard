using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The constraints, exercised against a real engine rather than read off the
/// model (ADR: The SQL Server backend).
///
/// SQLite is the engine here for the reason it is the engine in CI: it is the
/// one this suite can create and throw away in a millisecond, on a machine with
/// no cloud credential. The foreign key and the concurrency token are configured
/// for both providers, so proving they bite on one is worth more than asserting
/// on both and exercising neither. What is genuinely SQL Server only, the
/// rowversion and the clustered index, is asserted from the model in
/// SqlServerModelTests.
/// </summary>
public class RelationalConstraintTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), $"theyard-constraints-{Guid.NewGuid():N}.db");

    /// <summary>Pooling off, so the file can be deleted without a process-wide ClearAllPools.</summary>
    private YardConnection Connection => new(YardProvider.Sqlite, $"Data Source={_file};Pooling=False");

    private YardDbContext Context() => new(Connection.Options());

    public RelationalConstraintTests()
    {
        using var db = Context();
        db.Database.Migrate();
        db.Users.Add(new YardUser
        {
            Id = "buyer-1",
            UserName = "buyer1@example.com",
            NormalizedUserName = "BUYER1@EXAMPLE.COM",
            Email = "buyer1@example.com",
            NormalizedEmail = "BUYER1@EXAMPLE.COM",
            CreatedAtMs = 1,
        });
        db.SaveChanges();
    }

    private static BidRow ABid(string userId = "buyer-1") => new()
    {
        UserId = userId,
        VehicleId = "veh-1",
        Amount = 10_000,
        BidCount = 1,
        WonBuyNow = false,
        AtMs = 1,
    };

    // #region foreign-key
    [Fact]
    public void A_bid_for_an_account_that_does_not_exist_is_refused_by_the_database()
    {
        using var db = Context();
        db.Bids.Add(ABid("nobody"));

        var thrown = Assert.Throws<DbUpdateException>(() => db.SaveChanges());

        // Not a nicety. Before the foreign key existed, a bid whose account had
        // been deleted stayed in this table forever, loaded into BidService at
        // every startup, and counted toward a vehicle's standing price on behalf
        // of nobody.
        Assert.Contains("FOREIGN KEY", thrown.InnerException?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deleting_an_account_takes_its_bids_with_it()
    {
        using (var db = Context())
        {
            db.Bids.Add(ABid());
            db.SaveChanges();
        }

        using (var db = Context())
        {
            db.Users.Remove(db.Users.Single(user => user.Id == "buyer-1"));
            db.SaveChanges();
        }

        using (var db = Context())
        {
            Assert.Empty(db.Bids);
        }
    }
    // #endregion foreign-key

    // #region concurrency
    [Fact]
    public void The_second_of_two_writers_that_read_the_same_bid_is_refused()
    {
        using (var db = Context())
        {
            db.Bids.Add(ABid());
            db.SaveChanges();
        }

        using var first = Context();
        using var second = Context();
        var mine = first.Bids.Single();
        var theirs = second.Bids.Single();

        mine.Amount = 11_000;
        mine.RowVersion = Guid.NewGuid().ToByteArray();
        first.SaveChanges();

        theirs.Amount = 10_500;
        theirs.RowVersion = Guid.NewGuid().ToByteArray();

        // Without the token this would succeed and the site would show 10,500 on
        // a vehicle that stands at 11,000: a lost update, and the one kind of
        // bug an auction cannot have.
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
    }

    [Fact]
    public void The_store_moves_the_token_on_every_save()
    {
        var store = new EfBidStore(new PlainFactory(Connection));

        store.Save("buyer-1", "veh-1", new BidState(10_000, 1, false, 1));
        byte[] first = TokenOf();
        store.Save("buyer-1", "veh-1", new BidState(11_000, 2, false, 2));
        byte[] second = TokenOf();

        Assert.NotEmpty(first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_store_gets_past_a_conflict_it_did_not_cause()
    {
        var store = new EfBidStore(new PlainFactory(Connection));
        store.Save("buyer-1", "veh-1", new BidState(10_000, 1, false, 1));

        // Somebody else moves the row between this store reading it and writing
        // it. The store's retry starts again from what is there now, which is
        // the only correct answer: the alternative is overwriting a bid that
        // was already accepted.
        using (var other = Context())
        {
            var row = other.Bids.Single();
            row.Amount = 10_800;
            row.RowVersion = Guid.NewGuid().ToByteArray();
            other.SaveChanges();
        }

        store.Save("buyer-1", "veh-1", new BidState(12_000, 3, false, 3));

        using var db = Context();
        Assert.Equal(12_000, db.Bids.Single().Amount);
    }
    // #endregion concurrency

    private byte[] TokenOf()
    {
        using var db = Context();
        return db.Bids.Single().RowVersion ?? [];
    }

    /// <summary>
    /// The smallest thing that satisfies IDbContextFactory, so a store can be
    /// built in a test without a service provider.
    /// </summary>
    private sealed class PlainFactory(YardConnection connection) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => new(connection.Options());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (IOException)
            {
            }
        }
    }
}
