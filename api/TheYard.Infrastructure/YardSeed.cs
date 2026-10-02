// Fills an empty catalogue from the JSON files on first boot. It has a file of its own because
// it is the one place the database's starting content is decided, and that should be readable
// without the startup timing and schema checks around it.
using Microsoft.EntityFrameworkCore;
using TheYard.Application;

namespace TheYard.Infrastructure;

/// <summary>
/// First boot fills the catalogue tables from the JSON catalogue files. The
/// JSON readers are still the source of truth for what a fresh database
/// contains, which keeps `npm run data` the way the dataset is regenerated and
/// means the seed cannot drift from the file it came from.
/// </summary>
public static class YardSeed
{
    // #region seed
    /// <summary>
    /// Fills the vehicle table and the photo table, each only when it is empty,
    /// in one save, and returns what was inserted and what each table now holds.
    /// </summary>
    public static async Task<SeedResult> EnsureSeededAsync(YardDbContext db, IVehicleSource vehicles, IPhotoManifestSource photos)
    {
        int vehiclesAdded = 0;
        int photosAdded = 0;

        // "Empty" rather than "new", so a database that half-filled because a
        // process died mid-seed is not left half-filled forever.
        if (!await db.Vehicles.AnyAsync())
        {
            var rows = (await vehicles.LoadAsync()).Select((vehicle, index) => vehicle.ToRow(index)).ToList();
            db.Vehicles.AddRange(rows);
            vehiclesAdded = rows.Count;
        }

        if (!await db.Photos.AnyAsync())
        {
            var rows = (await photos.LoadAsync())
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
            await db.SaveChangesAsync();
        }

        return new SeedResult(vehiclesAdded, photosAdded, await db.Vehicles.CountAsync(), await db.Photos.CountAsync());
    }
    // #endregion seed
}
