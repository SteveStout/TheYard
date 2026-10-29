// Brings the relational database up once at startup, or reports that it could not. It has a file
// of its own because it runs before the container is built and its answer decides which stores
// get registered, which is a different job from the adapters that read and write the rows.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>
/// Bring the database up, or report that it could not be brought up. Called
/// once, before the container is built, because the answer decides what gets
/// registered.
/// </summary>
public static class YardDatabase
{
    // #region prepare
    /// <summary>
    /// Opens the database, brings the schema up or checks it is there, seeds an
    /// empty catalogue, and times both halves. Never throws: a failure comes
    /// back as a state that is not ready, with the exception attached for the log.
    /// </summary>
    public static async Task<DatabaseState> PrepareAsync(YardConnection connection,
        IVehicleSource seedVehicles,
        IPhotoManifestSource seedPhotos, CancellationToken cancellation)
    {
        try
        {
            using var db = new YardDbContext(connection.Options());

            // Both halves are timed because both are new work on every cold
            // start, and a container that takes longer to answer its first
            // request is a cost this change has to be able to state.
            var migrating = System.Diagnostics.Stopwatch.StartNew();
            string schemaNote = await BringSchemaUpAsync(db, connection, cancellation);
            migrating.Stop();
            var seeding = System.Diagnostics.Stopwatch.StartNew();
            var seeded = await YardSeed.EnsureSeededAsync(db, seedVehicles, seedPhotos, cancellation);
            seeding.Stop();

            return new DatabaseState(
                true,
                $"{connection.Describe()}, {schemaNote} in {migrating.ElapsedMilliseconds} ms "
                + $"and seeded in {seeding.ElapsedMilliseconds} ms, "
                + $"inserting {seeded.VehiclesInserted} vehicles and {seeded.PhotosInserted} photos, "
                + $"now holding {seeded.VehiclesTotal} and {seeded.PhotosTotal}")
            {
                SchemaMs = migrating.ElapsedMilliseconds,
                SeedMs = seeding.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            // Deliberately every exception. The caller's job is to keep serving
            // without a store, and it cannot do that if this throws. What went
            // wrong travels back as a sentence, is logged as an error, and shows
            // up as a failed health check on the Admin tab.
            // The type, not the message. See DatabaseState.Note: the message
            // goes back as the exception so a log can have it and a public page
            // cannot.
            return new DatabaseState(false, $"{connection.Describe()}: {ex.GetType().Name}", ex);
        }
    }
    // #endregion prepare

    // #region schema
    /// <summary>
    /// How the schema gets there, which is different per provider and is the
    /// whole of the difference (ADR: Data first, and the database in source
    /// control).
    ///
    /// On SQL Server it does not get there from here at all. The schema is
    /// `api/TheYard.Database`, published by SqlPackage, and this process holds
    /// `db_datareader` and `db_datawriter` and nothing else: it cannot create a
    /// table, so the only honest thing it can do is check that the schema it
    /// maps to is present and refuse the store if it is not. A container that
    /// silently created its own tables would be a second authority for the
    /// schema, and two authorities is the drift you cannot test your way out of.
    ///
    /// On SQLite it applies its own migrations, because a SQLite database here
    /// is created and thrown away by the process that uses it: a scratch file
    /// per test, and a container-lifetime file in the fallback. Nothing
    /// publishes to it and nothing else reads it.
    /// </summary>
    private static async Task<string> BringSchemaUpAsync(YardDbContext db, YardConnection connection, CancellationToken cancellation)
    {
        if (connection.Provider == YardProvider.Sqlite)
        {
            await db.Database.MigrateAsync(cancellation);
            return "migrated";
        }

        // The names are listed once, here, and not again inside the SQL: two
        // lists can drift, and this is a check whose whole job is to notice
        // drift.
        string[] required = ["Vehicles", "Photos", "Bids", "Standings", "AspNetUsers"];
        var present = await db.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.tables").ToListAsync(cancellation);
        var missing = required.Where(table => !present.Contains(table, StringComparer.OrdinalIgnoreCase)).ToList();
        return missing.Count == 0
            ? "found the published schema"
            : throw new InvalidOperationException(
                $"the published schema is missing {string.Join(", ", missing)}. "
                + "Publish api/TheYard.Database before pointing a container at this database.");
    }
    // #endregion schema

    /// <summary>Whether the seed catalogue is in the database right now: one vehicle and one photo row.</summary>
    public static async Task<bool> HasSeedAsync(IDbContextFactory<YardDbContext> contexts, CancellationToken cancellation)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellation);
        return await db.Vehicles.AnyAsync() && await db.Photos.AnyAsync();
    }

    /// <summary>
    /// One round trip to the database doing as little work as a round trip can, for the
    /// performance proof's measure of the network between the container and the store.
    /// </summary>
    public static async Task PingAsync(IDbContextFactory<YardDbContext> contexts, CancellationToken cancellation)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellation);
        await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellation);
    }

    /// <summary>
    /// Closes the pooled connections to one SQLite file, and only that file's, so it can be
    /// deleted while every other database in the process keeps its pool.
    /// </summary>
    /// <param name="file">The database file.</param>
    public static void ReleaseFile(string file)
    {
        using var connection = new SqliteConnection($"Data Source={file}");
        SqliteConnection.ClearPool(connection);
    }
}
