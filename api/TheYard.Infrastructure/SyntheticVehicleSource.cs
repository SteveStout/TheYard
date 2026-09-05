using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Infrastructure;

/// <summary>
/// Scales the seed dataset up to <paramref name="targetCount"/> records by
/// deterministically varying each seed vehicle's identity and numbers while
/// keeping the seed's make/model/trim distributions. Everything derives from
/// FNV-1a of the new id, so every run (and every machine) produces the same
/// inventory: 100k records with no 100 MB file in the repository.
/// </summary>
public sealed class SyntheticVehicleSource(IVehicleSource seedSource, int targetCount) : IVehicleSource
{
    private const string VinChars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";

    public IReadOnlyList<Vehicle> Load()
    {
        var seeds = seedSource.Load();
        if (targetCount <= seeds.Count)
        {
            return seeds;
        }

        var vehicles = new List<Vehicle>(targetCount);
        vehicles.AddRange(seeds);
        for (int index = seeds.Count; index < targetCount; index++)
        {
            vehicles.Add(Variant(seeds[index % seeds.Count], index));
        }
        return vehicles;
    }

    private static Vehicle Variant(Vehicle seed, int index)
    {
        string id = $"{seed.Id}-{index:x5}";
        uint hash = Fnv1a.Hash(id);

        int startingBid = 2_500 + (int)(hash % 95) * 500;
        bool hasBids = hash % 9 < 4; // ~44%, matching the seed dataset's mix
        int? currentBid = hasBids ? startingBid + (int)(hash % 40) * 500 : null;
        bool hasReserve = (hash >> 5) % 10 < 7;
        bool hasBuyNow = (hash >> 7) % 5 == 0;

        return seed with
        {
            Id = id,
            Vin = MutateVin(seed.Vin, hash),
            Year = Math.Clamp(seed.Year + (int)(hash % 7) - 3, 2016, 2026),
            OdometerKm = 500 + (int)(hash % 260_000),
            // #region grade-stays-with-its-words
            // Inside the seed's own band, not rolled across the whole scale.
            //
            // This used to be `1.0 + hash % 41 / 10.0`, a grade drawn with no
            // reference to anything else about the vehicle, while the condition
            // report, the damage notes and the title status all came from the
            // seed unchanged. So a variant could show "2.0 Rough" above
            // "Vehicle shows average wear for its age and mileage. Mechanically
            // sound." and "No damage reported.", which is three fields
            // disagreeing on one page.
            //
            // The seed dataset already correlates them: band 1 reads as
            // salvage, band 2 as rough with damage, band 3 as average, band 4
            // and 5 as clean, and the damage note counts fall from four to
            // none across that range. Varying inside the band keeps every one
            // of those correlations for free and costs one line.
            ConditionGrade = Math.Round(Math.Min(5.0, Math.Floor(seed.ConditionGrade) + hash % 10 / 10.0), 1),
            // #endregion grade-stays-with-its-words
            StartingBid = startingBid,
            CurrentBid = currentBid,
            BidCount = hasBids ? 1 + (int)(hash % 24) : 0,
            ReservePrice = hasReserve ? startingBid + 4_000 + (int)(hash % 20) * 500 : null,
            BuyNowPrice = hasBuyNow ? startingBid + 12_000 + (int)(hash % 16) * 500 : null,
            Lot = $"{(char)('A' + (int)((hash >> 9) % 6))}-{hash % 10_000:D4}",
        };
    }

    private static string MutateVin(string vin, uint hash)
    {
        char[] chars = vin.ToCharArray();
        for (int i = 0; i < 6 && i < chars.Length; i++)
        {
            chars[chars.Length - 1 - i] = VinChars[(int)((hash >> (i * 5)) % (uint)VinChars.Length)];
        }
        return new string(chars);
    }
}
