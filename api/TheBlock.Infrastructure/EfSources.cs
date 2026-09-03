using Microsoft.EntityFrameworkCore;
using TheBlock.Application;
using TheBlock.Data;

namespace TheBlock.Infrastructure;

/// <summary>
/// Row to domain and back. The mapping is dull on purpose and lives in one
/// place, because the interesting failure mode of a persistence layer is a
/// field that quietly stops being copied.
/// </summary>
public static class VehicleRows
{
    // #region mapping
    public static Vehicle ToVehicle(this VehicleRow row) => new()
    {
        Id = row.Id,
        Vin = row.Vin,
        Year = row.Year,
        Make = row.Make,
        Model = row.Model,
        Trim = row.Trim,
        BodyStyle = row.BodyStyle,
        ExteriorColor = row.ExteriorColor,
        InteriorColor = row.InteriorColor,
        Engine = row.Engine,
        Transmission = row.Transmission,
        Drivetrain = row.Drivetrain,
        OdometerKm = row.OdometerKm,
        FuelType = row.FuelType,
        ConditionGrade = row.ConditionGrade,
        ConditionReport = row.ConditionReport,
        DamageNotes = row.DamageNotes,
        TitleStatus = row.TitleStatus,
        Province = row.Province,
        City = row.City,
        AuctionStart = row.AuctionStart,
        StartingBid = row.StartingBid,
        ReservePrice = row.ReservePrice,
        BuyNowPrice = row.BuyNowPrice,
        Images = row.Images,
        SellingDealership = row.SellingDealership,
        Lot = row.Lot,
        CurrentBid = row.CurrentBid,
        BidCount = row.BidCount,
    };

    public static VehicleRow ToRow(this Vehicle vehicle, int seq) => new()
    {
        Seq = seq,
        Id = vehicle.Id,
        Vin = vehicle.Vin,
        Year = vehicle.Year,
        Make = vehicle.Make,
        Model = vehicle.Model,
        Trim = vehicle.Trim,
        BodyStyle = vehicle.BodyStyle,
        ExteriorColor = vehicle.ExteriorColor,
        InteriorColor = vehicle.InteriorColor,
        Engine = vehicle.Engine,
        Transmission = vehicle.Transmission,
        Drivetrain = vehicle.Drivetrain,
        OdometerKm = vehicle.OdometerKm,
        FuelType = vehicle.FuelType,
        ConditionGrade = vehicle.ConditionGrade,
        ConditionReport = vehicle.ConditionReport,
        DamageNotes = [.. vehicle.DamageNotes],
        TitleStatus = vehicle.TitleStatus,
        Province = vehicle.Province,
        City = vehicle.City,
        AuctionStart = vehicle.AuctionStart,
        StartingBid = vehicle.StartingBid,
        ReservePrice = vehicle.ReservePrice,
        BuyNowPrice = vehicle.BuyNowPrice,
        Images = [.. vehicle.Images],
        SellingDealership = vehicle.SellingDealership,
        Lot = vehicle.Lot,
        CurrentBid = vehicle.CurrentBid,
        BidCount = vehicle.BidCount,
    };
    // #endregion mapping
}

/// <summary>
/// Adapter: the seed catalogue, out of the database. Same port the JSON file
/// reader implements, and the synthetic scale-up still wraps it, so nothing
/// above this line changed when the storage did.
/// </summary>
public sealed class EfVehicleSource(IDbContextFactory<YardDbContext> factory) : IVehicleSource
{
    // #region ef-sources
    public IReadOnlyList<Vehicle> Load()
    {
        using var db = factory.CreateDbContext();
        // Read once, in seed order, tracking nothing: these rows are a
        // catalogue this process will never write back.
        return [.. db.Vehicles.AsNoTracking().OrderBy(row => row.Seq).Select(row => row.ToVehicle())];
    }
    // #endregion ef-sources
}

/// <summary>Adapter: the photo manifest, out of the database.</summary>
public sealed class EfPhotoManifestSource(IDbContextFactory<YardDbContext> factory) : IPhotoManifestSource
{
    public IReadOnlyList<PhotoEntry> Load()
    {
        using var db = factory.CreateDbContext();
        return
        [
            .. db.Photos.AsNoTracking().OrderBy(row => row.Seq)
                .Select(row => new PhotoEntry(row.File, row.Style, row.Title)),
        ];
    }
}

/// <summary>
/// Adapter: bids, which are the only thing here that survives a restart
/// because they are the only thing a visitor creates.
/// </summary>
public sealed class EfBidStore(IDbContextFactory<YardDbContext> factory) : IBidStore
{
    // #region bid-store
    public IReadOnlyList<StoredBid> Load()
    {
        using var db = factory.CreateDbContext();
        return
        [
            .. db.Bids.AsNoTracking()
                .Select(row => new StoredBid(
                    row.UserId,
                    row.VehicleId,
                    new BidState(row.Amount, row.BidCount, row.WonBuyNow, row.AtMs))),
        ];
    }

    /// <summary>
    /// One row per buyer per vehicle, replaced rather than appended. Called
    /// inside BidService's lock, which is what makes "read the row, decide,
    /// write the row" safe here without the database needing an opinion about
    /// it.
    /// </summary>
    public void Save(string userId, string vehicleId, BidState state)
    {
        // Three tries, then the exception travels. A conflict here means
        // another writer changed this row between this one reading it and
        // writing it, and the answer to that is to start again from what is
        // there now, not to overwrite it. A fresh context per attempt, because
        // a context that has just thrown a concurrency exception is holding the
        // values that lost.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Write(userId, vehicleId, state);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Deliberately empty. The retry is the handling.
            }
        }
    }

    private void Write(string userId, string vehicleId, BidState state)
    {
        using var db = factory.CreateDbContext();
        var existing = db.Bids.Find(userId, vehicleId);
        if (existing is null)
        {
            existing = new BidRow
            {
                UserId = userId,
                VehicleId = vehicleId,
                Amount = state.Amount,
                BidCount = state.BidCount,
                WonBuyNow = state.WonBuyNow,
                AtMs = state.AtMs,
            };
            db.Bids.Add(existing);
        }
        else
        {
            existing.Amount = state.Amount;
            existing.BidCount = state.BidCount;
            existing.WonBuyNow = state.WonBuyNow;
            existing.AtMs = state.AtMs;
        }

        // SQL Server keeps the token itself; SQLite has no rowversion type, so
        // the store is what moves it. Setting it here rather than in a trigger
        // keeps the difference between the two providers in one readable place
        // (ADR: The SQL Server backend).
        if (db.Database.IsSqlite())
        {
            existing.RowVersion = Guid.NewGuid().ToByteArray();
        }

        db.SaveChanges();
    }

    public void Clear()
    {
        using var db = factory.CreateDbContext();
        db.Bids.ExecuteDelete();
    }
    // #endregion bid-store
}

/// <summary>
/// Whether the store is usable, and one sentence about why. The composition
/// root asks this before it registers anything, so a database that will not
/// open changes which adapters are wired rather than becoming a 500 on the
/// first request (ADR: The relational store).
/// </summary>
public sealed record DatabaseState(bool Ready, string Note);

/// <summary>
/// Bring the database up, or report that it could not be brought up. Called
/// once, before the container is built, because the answer decides what gets
/// registered.
/// </summary>
public static class YardDatabase
{
    // #region prepare
    public static DatabaseState Prepare(
        YardConnection connection,
        IVehicleSource seedVehicles,
        IPhotoManifestSource seedPhotos)
    {
        try
        {
            using var db = new YardDbContext(connection.Options());

            // Both halves are timed because both are new work on every cold
            // start, and a container that takes longer to answer its first
            // request is a cost this change has to be able to state.
            var migrating = System.Diagnostics.Stopwatch.StartNew();
            string schemaNote = BringSchemaUp(db, connection);
            migrating.Stop();
            var seeding = System.Diagnostics.Stopwatch.StartNew();
            var seeded = YardSeed.EnsureSeeded(db, seedVehicles, seedPhotos);
            seeding.Stop();

            return new DatabaseState(
                true,
                $"{connection.Describe()}, {schemaNote} in {migrating.ElapsedMilliseconds} ms "
                + $"and seeded in {seeding.ElapsedMilliseconds} ms, "
                + $"inserting {seeded.VehiclesInserted} vehicles and {seeded.PhotosInserted} photos, "
                + $"now holding {seeded.VehiclesTotal} and {seeded.PhotosTotal}");
        }
        catch (Exception ex)
        {
            // Deliberately every exception. The caller's job is to keep serving
            // without a store, and it cannot do that if this throws. What went
            // wrong travels back as a sentence, is logged as an error, and shows
            // up as a failed health check on the Admin tab.
            return new DatabaseState(false, $"{connection.Describe()}: {ex.GetType().Name}: {ex.Message}");
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
    /// `api/TheBlock.Database`, published by SqlPackage, and this process holds
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
    private static string BringSchemaUp(YardDbContext db, YardConnection connection)
    {
        if (connection.Provider == YardProvider.Sqlite)
        {
            db.Database.Migrate();
            return "migrated";
        }

        // The names are listed once. An earlier version had them in the array
        // and again inside the SQL, which is two lists that can drift, on a
        // check whose whole job is to notice drift.
        string[] required = ["Vehicles", "Photos", "Bids", "AspNetUsers"];
        var present = db.Database.SqlQuery<string>($"SELECT name AS Value FROM sys.tables").ToList();
        var missing = required.Where(table => !present.Contains(table, StringComparer.OrdinalIgnoreCase)).ToList();
        return missing.Count == 0
            ? "found the published schema"
            : throw new InvalidOperationException(
                $"the published schema is missing {string.Join(", ", missing)}. "
                + "Publish api/TheBlock.Database before pointing a container at this database.");
    }
    // #endregion schema
}

/// <summary>What the first boot found, so the log line can say it.</summary>
public sealed record SeedResult(int VehiclesInserted, int PhotosInserted, int VehiclesTotal, int PhotosTotal);

/// <summary>
/// First boot fills the catalogue tables from the files that used to be the
/// catalogue. The JSON readers are still the source of truth for what a fresh
/// database contains, which keeps `npm run data` the way the dataset is
/// regenerated and means the seed cannot drift from the file it came from.
/// </summary>
public static class YardSeed
{
    // #region seed
    public static SeedResult EnsureSeeded(YardDbContext db, IVehicleSource vehicles, IPhotoManifestSource photos)
    {
        int vehiclesAdded = 0;
        int photosAdded = 0;

        // "Empty" rather than "new", so a database that half-filled because a
        // process died mid-seed is not left half-filled forever.
        if (!db.Vehicles.Any())
        {
            var rows = vehicles.Load().Select((vehicle, index) => vehicle.ToRow(index)).ToList();
            db.Vehicles.AddRange(rows);
            vehiclesAdded = rows.Count;
        }

        if (!db.Photos.Any())
        {
            var rows = photos.Load()
                .Select((photo, index) => new PhotoRow
                {
                    Seq = index,
                    File = photo.File,
                    Style = photo.Style,
                    Title = photo.Title,
                })
                .ToList();
            db.Photos.AddRange(rows);
            photosAdded = rows.Count;
        }

        if (vehiclesAdded > 0 || photosAdded > 0)
        {
            db.SaveChanges();
        }

        return new SeedResult(vehiclesAdded, photosAdded, db.Vehicles.Count(), db.Photos.Count());
    }
    // #endregion seed
}
