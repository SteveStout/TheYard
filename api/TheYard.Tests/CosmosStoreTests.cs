using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Tests;

// #region cosmos-fact
/// <summary>
/// A test that needs the real account. CI has no Azure credential and is not
/// getting one, so CI runs the suite with these filtered out by their trait
/// (`--filter "Store!=cosmos"`), and the ship gate on the runner runs them as
/// the signed-in principal against the tests- containers, whose documents
/// expire in a day. They are not skipped: run with no endpoint they fail and
/// say why, because a store test that passes with no store is a check that
/// cannot fail (ADR: A second store on Cosmos DB, and what it costs).
/// </summary>
internal static class CosmosLive
{
    public const string Trait = "Store";
    public const string Value = "cosmos";

    public static string Endpoint =>
        Environment.GetEnvironmentVariable("Cosmos__AccountEndpoint")
        ?? throw new InvalidOperationException(
            "this test needs Cosmos__AccountEndpoint in the environment; it runs on the runner as the signed-in principal, and CI filters it out with --filter \"Store!=cosmos\"");

    /// <summary>A store over the test containers, with its own ring so a test can read what it ran.</summary>
    public static (CosmosStore Store, StoreRingBuffer Log) Open()
    {
        var log = new StoreRingBuffer(500);
        var store = CosmosStore.Connect(
            Endpoint,
            Environment.GetEnvironmentVariable("Cosmos__Database") ?? "theyard",
            "tests-",
            Environment.GetEnvironmentVariable("Cosmos__Credential") ?? "azure-cli",
            Environment.GetEnvironmentVariable("Azure__ClientId") ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
            log);
        return (store, log);
    }

    public static async Task<CosmosStore> Prepared()
    {
        var (store, _) = Open();
        var state = await store.PrepareAsync(
            new JsonFileVehicleSource(Repo.DataFile("vehicles.json")),
            new JsonFilePhotoManifestSource(Path.Combine(Repo.Root(), "api", "TheYard.Api", "photo-manifest.json")));
        Assert.True(state.Ready, state.Note);
        return store;
    }
}
// #endregion cosmos-fact

/// <summary>The three adapters and the account store, against the real account.</summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public class CosmosStoreTests
{
    // #region prepare-and-seed
    [Fact]
    public async Task The_store_comes_up_against_the_test_containers_and_seeds_only_when_they_are_empty()
    {
        var (store, log) = CosmosLive.Open();
        var vehicles = new JsonFileVehicleSource(Repo.DataFile("vehicles.json"));
        var photos = new JsonFilePhotoManifestSource(Path.Combine(Repo.Root(), "api", "TheYard.Api", "photo-manifest.json"));

        var first = await store.PrepareAsync(vehicles, photos);
        Assert.True(first.Ready, first.Note);
        Assert.StartsWith("Azure Cosmos DB, found 4 containers", first.Note);
        Assert.Contains("now holding 200 and 50", first.Note);

        // The second boot finds the seed and writes nothing, whatever the first one did.
        var second = await store.PrepareAsync(vehicles, photos);
        Assert.True(second.Ready, second.Note);
        Assert.Contains("inserting 0 vehicles and 0 photos", second.Note);
        Assert.Equal(0, second.SeedRequestUnits);

        // The container checks are in the log as metadata reads, before anything else.
        var operations = log.Snapshot();
        Assert.Contains(operations, o => o.Kind == StoreOperationKind.Metadata && o.Container == "vehicles");
    }
    // #endregion prepare-and-seed

    // #region catalogue
    [Fact]
    public async Task The_catalogue_comes_back_in_seed_order_and_without_the_images_it_never_shows()
    {
        var store = await CosmosLive.Prepared();
        var fromFile = await new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).LoadAsync();

        var loaded = await new CosmosVehicleSource(store).LoadAsync();

        Assert.Equal(fromFile.Select(v => v.Id), loaded.Select(v => v.Id));
        Assert.All(loaded, v => Assert.Empty(v.Images));
        // The lists by sequence and the rest by the record, for the reason the
        // relational round trip gives: a list's Equals is reference equality.
        IReadOnlyList<string> none = Array.Empty<string>();
        for (int i = 0; i < fromFile.Count; i++)
        {
            Assert.Equal(fromFile[i].DamageNotes, loaded[i].DamageNotes);
            Assert.Equal(
                fromFile[i] with { DamageNotes = none, Images = none },
                loaded[i] with { DamageNotes = none, Images = none });
        }

        var manifest = await new CosmosPhotoManifestSource(store).LoadAsync();
        Assert.Equal(50, manifest.Count);
    }
    // #endregion catalogue

    // #region bids
    [Fact]
    public async Task A_bid_is_written_read_back_replaced_and_cleared_inside_the_buyers_partition()
    {
        var (store, log) = CosmosLive.Open();
        var bids = new CosmosBidStore(store);
        string buyer = $"buyer-{Guid.NewGuid():N}";

        await bids.SaveAsync(buyer, "veh-1", new BidState(10_000, 1, false, 1));
        await bids.SaveAsync(buyer, "veh-1", new BidState(11_000, 2, false, 2));
        await bids.SaveAsync(buyer, "veh-2", new BidState(5_000, 1, false, 3));

        var mine = (await bids.LoadAsync()).Where(b => b.UserId == buyer).OrderBy(b => b.VehicleId).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal(11_000, mine[0].State.Amount);
        Assert.Equal(2, mine[0].State.BidCount);

        await bids.ClearAsync(buyer);
        Assert.DoesNotContain((await bids.LoadAsync()), b => b.UserId == buyer);

        var operations = log.Snapshot();
        // Every write was pinned to the buyer; the second save was a replace carrying the etag.
        Assert.Contains(operations, o => o.Container == "bids" && o.Kind == StoreOperationKind.PointWrite && o.Text == "CreateItem" && o.Partition == "pinned to the buyer");
        Assert.Contains(operations, o => o.Container == "bids" && o.Text == "ReplaceItem (If-Match)");
        // The reset was one query inside the partition and one batch of deletes.
        Assert.Contains(operations, o => o.Container == "bids" && o.Kind == StoreOperationKind.Query && o.Partition == "pinned to the buyer");
        Assert.Contains(operations, o => o.Container == "bids" && o.Kind == StoreOperationKind.Batch);
        // And the only fan-out was the load.
        Assert.All(operations.Where(o => o.Partition.StartsWith("cross", StringComparison.Ordinal)), o => Assert.Equal(StoreOperationKind.Query, o.Kind));
        Assert.All(operations, o => Assert.True(o.RequestCharge > 0, $"{o.Kind} on {o.Container} reported no charge"));
    }
    // #endregion bids

    // #region accounts
    private static ServiceProvider Identity(CosmosStore store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store);
        services.AddIdentityCore<YardUser>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireDigit = false;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.AllowedForNewUsers = true;
        }).AddUserStore<CosmosUserStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task An_account_is_one_document_and_one_claim_and_the_second_claim_on_an_address_is_refused()
    {
        var (store, log) = CosmosLive.Open();
        using var provider = Identity(store);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<YardUser>>();
        string email = $"leak-canary-{Guid.NewGuid():N}@example.com";

        var created = await users.CreateAsync(new YardUser { UserName = email, Email = email, CreatedAtMs = 1 }, "correct horse");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        var again = await users.CreateAsync(new YardUser { UserName = email, Email = email, CreatedAtMs = 2 }, "correct horse");
        Assert.False(again.Succeeded);
        Assert.Contains(again.Errors, e => e.Code.Contains("Duplicate", StringComparison.Ordinal));

        var found = await users.FindByEmailAsync(email);
        Assert.NotNull(found);
        Assert.True(await users.CheckPasswordAsync(found, "correct horse"));
        Assert.False(await users.CheckPasswordAsync(found, "wrong"));

        // A failed guess moves the count, through a replace that carries the etag.
        await users.AccessFailedAsync(found);
        var after = await users.FindByIdAsync(found.Id);
        Assert.Equal(1, after!.AccessFailedCount);
        await users.ResetAccessFailedCountAsync(after);

        // "Who am I" is one point read; sign-in by address is two.
        var operations = log.Snapshot();
        Assert.Contains(operations, o => o.Container == "users" && o.Text == "CreateItem (email claim)");
        Assert.Contains(operations, o => o.Container == "users" && o.Text == "CreateItem (account)");
        Assert.Contains(operations, o => o.Container == "users" && o.Kind == StoreOperationKind.PointWrite && o.Text == "ReplaceItem (If-Match)");
        Assert.DoesNotContain(operations, o => o.Container == "users" && o.Kind == StoreOperationKind.Query);

        // The no-values rule, on the store that has the address as a document id.
        foreach (var operation in operations)
        {
            Assert.DoesNotContain(email, operation.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(email, operation.Partition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("leak-canary", operation.Text, StringComparison.OrdinalIgnoreCase);
        }

        var deleted = await users.DeleteAsync(after);
        Assert.True(deleted.Succeeded);
        Assert.Null(await users.FindByEmailAsync(email));
        // The address is free again: the claim went with the account.
        var third = await users.CreateAsync(new YardUser { UserName = email, Email = email, CreatedAtMs = 3 }, "correct horse");
        Assert.True(third.Succeeded);
        await users.DeleteAsync((await users.FindByEmailAsync(email))!);
    }
    // #endregion accounts
}
