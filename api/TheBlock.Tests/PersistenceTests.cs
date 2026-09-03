using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TheBlock.Infrastructure;

namespace TheBlock.Tests;

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

    private WebApplicationFactory<Program> Api() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Yard", $"Data Source={_file}"));

    private YardDbContext Context() =>
        new(new DbContextOptionsBuilder<YardDbContext>().UseSqlite($"Data Source={_file}").Options);

    // #region restart
    [Fact]
    public async Task A_bid_survives_the_api_restarting()
    {
        long anchor = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        string id;
        int amount;

        await using (var first = Api())
        {
            var client = first.CreateClient();
            using var page = JsonDocument.Parse(
                await client.GetStringAsync($"/api/vehicles?status=live&limit=1&anchor_ms={anchor}"));
            var vehicle = page.RootElement.GetProperty("vehicles")[0];
            id = vehicle.GetProperty("id").GetString()!;
            amount = vehicle.GetProperty("min_next_bid").GetInt32();

            var placed = await client.PostAsJsonAsync(
                $"/api/vehicles/{id}/bids", new { amount, anchor_ms = anchor });
            Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        }

        // A second application. It shares nothing with the first except the
        // bytes on disk, which is the entire claim under test.
        await using var second = Api();
        string bids = await second.CreateClient().GetStringAsync("/api/bids");

        Assert.Contains(id, bids, StringComparison.Ordinal);
        Assert.Contains(amount.ToString(CultureInfo.InvariantCulture), bids, StringComparison.Ordinal);
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

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
            var client = first.CreateClient();
            using var page = JsonDocument.Parse(
                await client.GetStringAsync($"/api/vehicles?status=live&limit=1&anchor_ms={anchor}"));
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

    private int Count()
    {
        using var db = Context();
        return db.Vehicles.Count();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // A test's scratch file that outlives the test is litter, not a failure.
        }
        GC.SuppressFinalize(this);
    }
}
