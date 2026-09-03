using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// What a database is for (ADR: The relational store): a bid placed against one
/// process is still there when the next one starts.
///
/// Every other test class in this suite gets its own scratch database without
/// asking for one and never notices the storage changed. This class is the
/// exception, and pins two application instances to the same file on purpose,
/// because a claim about surviving a restart can only be tested by restarting.
/// </summary>
public class PersistenceTests : IDisposable
{
    private readonly string _file =
        Path.Combine(Path.GetTempPath(), $"theyard-restart-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Pooling off, for the same reason the application turns it off for a
    /// scratch database: the alternative is ClearAllPools, which is process
    /// wide, and xUnit runs test classes in parallel in one process. A class
    /// that clears the pool to tidy up its own file clears everybody's.
    /// </summary>
    private string Connection => $"Data Source={_file};Pooling=False";

    private WebApplicationFactory<Program> Api() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Yard", Connection));

    /// <summary>
    /// A live vehicle with time left on it. Most bids rather than the default
    /// sort, which is EndingSoonest and so returns the one auction in the
    /// dataset with the least time on it. AuctionClock carries two instants
    /// and only one of them is the anchor these tests pin: NowMs is real
    /// wall-clock, read per request, and liveness is judged against it. Under
    /// the full suite that vehicle closes between reading min_next_bid and
    /// posting the bid, about one run in three.
    /// </summary>
    private static string ALiveVehicleQuery(long anchor) =>
        $"/api/vehicles?status=live&sort=most-bids&limit=1&anchor_ms={anchor}";

    private YardDbContext Context() =>
        new(new DbContextOptionsBuilder<YardDbContext>().UseSqlite(Connection).Options);

    // #region restart
    [Fact]
    public async Task A_bid_survives_the_api_restarting()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        string id;
        int amount;
        string email;

        await using (var first = Api())
        {
            var client = await Buyers.SignedIn(first);
            using var page = JsonDocument.Parse(
                await client.GetStringAsync(ALiveVehicleQuery(anchor)));
            var live = page.RootElement.GetProperty("vehicles");
            // The auction clock is anchored per request, so a live vehicle is
            // always there. Saying so out loud costs nothing and turns a future
            // index-out-of-range into a sentence (the staff review).
            Assert.True(live.GetArrayLength() > 0, "the anchored clock should always have a live auction");
            var vehicle = live[0];
            id = vehicle.GetProperty("id").GetString()!;
            amount = vehicle.GetProperty("min_next_bid").GetInt32();

            var placed = await client.PostAsJsonAsync(
                $"/api/vehicles/{id}/bids", new { amount, anchor_ms = anchor });
            // The reason, not just the number. Every rejection on this API
            // carries its sentence in `detail` (ADR: Error handling), and a
            // bare "Expected OK, actual BadRequest" is a test that knows
            // something is wrong and will not say what.
            Assert.True(
                placed.StatusCode == HttpStatusCode.OK,
                $"the bid of {amount} was refused: {await placed.Content.ReadAsStringAsync()}\n"
                    + $"the vehicle was {vehicle}");
            email = await WhoAmI(client);
        }

        // A second application. It shares nothing with the first except the
        // bytes on disk, which is the entire claim under test. The same person
        // signs in again, because a bid that survived and could not be found by
        // its owner would not have survived in any useful sense.
        await using var second = Api();
        var returning = second.CreateClient();
        (await returning.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "correct horse" })).EnsureSuccessStatusCode();
        string bids = await returning.GetStringAsync("/api/bids");

        // Parsed rather than matched as a substring: a thirteen-digit
        // timestamp contains almost any five-digit amount somewhere inside it,
        // so the old assertion could pass on a bid that had not survived at all
        // (the staff review, 2026-09-03).
        using var restored = JsonDocument.Parse(bids);
        Assert.True(
            restored.RootElement.TryGetProperty(id, out var mine),
            $"the restored process has no bid for {id}: {bids}");
        Assert.Equal(amount, mine.GetProperty("amount").GetInt32());
    }
    // #endregion restart

    [Fact]
    public async Task The_catalogue_is_seeded_on_the_first_boot_and_left_alone_on_the_second()
    {
        await using (var first = Api())
        {
            first.CreateClient().Dispose();
        }

        int afterFirst = Count();

        await using (var second = Api())
        {
            second.CreateClient().Dispose();
        }

        int afterSecond = Count();

        Assert.True(afterFirst > 0, "the first boot should have filled the catalogue");
        // Seeding asks whether the table is empty, not whether the file is new,
        // so the second boot has to add nothing at all rather than duplicating.
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task The_catalogue_is_on_disk_and_not_only_in_the_answer()
    {
        await using (var api = Api())
        {
            api.CreateClient().Dispose();
        }

        long bytes = OnDisk();

        using var db = Context();
        int vehicles = db.Vehicles.Count();
        int photos = db.Photos.Count();

        // A boot that answers correctly out of an empty file would mean the data
        // never left memory, which is the one failure a persistence layer can
        // have while looking entirely healthy.
        Assert.True(vehicles > 100, $"the seed catalogue should be in the table; it holds {vehicles}");
        Assert.True(photos > 0, $"the photo manifest should be in the table; it holds {photos}");
        Assert.True(
            bytes > 64_000,
            $"a seeded catalogue is more than a page of SQLite; the database and its log come to {bytes} bytes");
    }

    /// <summary>
    /// The database and its write-ahead log, which are one thing between them.
    /// SQLite commits into the log and checkpoints into the database later, so
    /// a hard stop can leave a one-page database beside a very full log. Both
    /// are the data (ADR: The relational store).
    /// </summary>
    private long OnDisk() =>
        new[] { _file, _file + "-wal" }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

    [Fact]
    public async Task The_schema_came_from_a_migration_rather_than_from_EnsureCreated()
    {
        await using var api = Api();
        api.CreateClient().Dispose();

        using var db = Context();
        // EnsureCreated would have produced the same tables and no history, and
        // a database with no history cannot be brought forward later.
        Assert.NotEmpty(db.Database.GetAppliedMigrations());
    }

    // #region mapping-round-trip
    /// <summary>
    /// The failure `VehicleRows` names in its own summary, which nothing was
    /// checking: "a field that quietly stops being copied". Comparing whole
    /// records by value catches a dropped field, a narrowed type and a swapped
    /// pair at once, which is what record equality is for (the staff review,
    /// 2026-09-03).
    /// </summary>
    [Fact]
    public void Every_field_survives_the_round_trip_through_a_row()
    {
        var source = new JsonFileVehicleSource(SeedPath()).Load();

        var roundTripped = source.Select((vehicle, index) => vehicle.ToRow(index).ToVehicle()).ToList();

        Assert.Equal(source.Count, roundTripped.Count);

        // Comparing the records outright fails, and not because a field is
        // dropped. Vehicle exposes DamageNotes and Images as
        // IReadOnlyList<string>; the default comparer for an interface type
        // calls the instance's own Equals, which for a list is reference
        // equality. Two vehicles with equal but distinct lists are therefore
        // not equal, so the two collections are compared by sequence and the
        // other twenty-seven fields are left to the record, with one shared
        // empty instance standing in so its equality can do its job.
        IReadOnlyList<string> none = Array.Empty<string>();
        for (int i = 0; i < source.Count; i++)
        {
            Assert.Equal(source[i].DamageNotes, roundTripped[i].DamageNotes);
            Assert.Equal(source[i].Images, roundTripped[i].Images);
            Assert.Equal(
                source[i] with { DamageNotes = none, Images = none },
                roundTripped[i] with { DamageNotes = none, Images = none });
        }
    }

    /// <summary>
    /// And the order, which is the other thing the model comments say matters:
    /// the synthetic scale-up expands the seed catalogue from its order, so a
    /// set that came back differently ordered would be a different hundred
    /// thousand vehicles.
    /// </summary>
    [Fact]
    public async Task The_store_returns_the_catalogue_in_the_order_the_file_had_it()
    {
        await using (var api = Api())
        {
            api.CreateClient().Dispose();
        }

        var fromFile = new JsonFileVehicleSource(SeedPath()).Load().Select(v => v.Id).ToList();
        using var db = Context();
        var fromStore = db.Vehicles.AsNoTracking().OrderBy(row => row.Seq).Select(row => row.Id).ToList();

        Assert.Equal(fromFile, fromStore);
    }
    /// <summary>The seed catalogue on disk, found the way the other suites find it.</summary>
    private static string SeedPath() =>
        Path.Combine(JsonFileSourceTests.RepoRoot(), "data", "vehicles.json");
    // #endregion mapping-round-trip

    // #region fallback
    /// <summary>
    /// The store not opening must not be able to take the site down
    /// (ADR: The relational store). A path with a file where a directory should
    /// be cannot be opened on any operating system, which makes this the same
    /// test everywhere.
    /// </summary>
    [Fact]
    public async Task A_database_that_will_not_open_leaves_the_site_serving_from_files()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"theyard-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "a file, standing where a directory would need to be");
        try
        {
            await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.UseSetting("ConnectionStrings:Yard", $"Data Source={Path.Combine(blocker, "yard.db")}"));
            var client = api.CreateClient();

            using var page = JsonDocument.Parse(await client.GetStringAsync("/api/vehicles?limit=5"));
            Assert.True(
                page.RootElement.GetProperty("total").GetInt32() > 0,
                "the inventory still has to answer when the store does not");

            var health = await client.GetAsync("/api/health");
            string body = await health.Content.ReadAsStringAsync();
            // Degraded and saying so is a different thing from degraded quietly.
            Assert.Contains("serving the catalogue from files", body, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blocker);
        }
    }
    // #endregion fallback

    [Fact]
    public async Task Clearing_the_bids_clears_them_in_the_store_too()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

        await using (var first = Api())
        {
            var client = await Buyers.SignedIn(first);
            using var page = JsonDocument.Parse(
                await client.GetStringAsync(ALiveVehicleQuery(anchor)));
            var vehicle = page.RootElement.GetProperty("vehicles")[0];
            string id = vehicle.GetProperty("id").GetString()!;
            int amount = vehicle.GetProperty("min_next_bid").GetInt32();

            await client.PostAsJsonAsync($"/api/vehicles/{id}/bids", new { amount, anchor_ms = anchor });
            (await client.DeleteAsync("/api/bids")).EnsureSuccessStatusCode();
        }

        await using var second = Api();
        string bids = await second.CreateClient().GetStringAsync("/api/bids");
        Assert.Equal("{}", bids.Trim());
    }

    /// <summary>The signed-in account's email, so the next process can sign in as them.</summary>
    private static async Task<string> WhoAmI(HttpClient client)
    {
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        return me.RootElement.GetProperty("email").GetString()!;
    }

    private int Count()
    {
        using var db = Context();
        return db.Vehicles.Count();
    }

    public void Dispose()
    {
        foreach (string leftover in new[] { _file, _file + "-wal", _file + "-shm" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A test's scratch file that outlives the test is litter, not a failure.
            }
        }
        GC.SuppressFinalize(this);
    }
}
