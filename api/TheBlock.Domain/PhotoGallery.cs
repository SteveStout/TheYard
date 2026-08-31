using TheBlock.Data;

namespace TheBlock.Domain;

/// <summary>
/// Selects a vehicle's photo gallery from the body-style pools. Pure domain
/// logic: deterministic per vehicle id, preferring photos whose source title
/// mentions the vehicle's make, filling the rest from the same pool.
/// </summary>
public static class PhotoGallery
{
    public const int GallerySize = 4;

    /// <summary>
    /// Photo file names for one vehicle, or an empty list when its body style
    /// has no pool (callers keep whatever images the dataset carries).
    /// </summary>
    public static IReadOnlyList<string> SelectPhotos(
        string vehicleId,
        string make,
        string bodyStyle,
        IReadOnlyDictionary<string, IReadOnlyList<PhotoEntry>> pools)
    {
        if (!pools.TryGetValue(bodyStyle.ToLowerInvariant(), out var pool) || pool.Count == 0)
        {
            return [];
        }

        string makeLower = make.ToLowerInvariant();
        var sameMake = pool.Where(photo => photo.Title.ToLowerInvariant().Contains(makeLower)).ToList();
        var others = pool.Where(photo => !sameMake.Contains(photo)).ToList();
        uint hash = Fnv1a.Hash(vehicleId);

        return Rotate(sameMake, hash)
            .Concat(Rotate(others, hash / 7))
            .Take(GallerySize)
            .Select(photo => photo.File)
            .ToList();
    }

    private static List<T> Rotate<T>(List<T> items, uint by)
    {
        if (items.Count == 0)
        {
            return items;
        }
        int offset = (int)(by % (uint)items.Count);
        return [.. items.Skip(offset), .. items.Take(offset)];
    }
}
