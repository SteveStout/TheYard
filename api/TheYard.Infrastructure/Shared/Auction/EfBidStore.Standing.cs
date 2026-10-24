// The standing half of the relational bid store: reading each vehicle's standing and moving it
// with a compare-and-set on its version. A file of its own because EfSources.cs holds the three
// adapters and the buyer rows, and this is the part two sites over one database meet in.
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>The standings table, read and moved; the adapter itself is described in EfSources.cs.</summary>
public sealed partial class EfBidStore
{
    // #region standing-store
    /// <summary>Every vehicle's standing, for the replay at startup.</summary>
    public async Task<IReadOnlyList<StoredStanding>> LoadStandingsAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        var rows = await db.Standings.AsNoTracking().ToListAsync();
        return rows.Select(Stored).ToList();
    }

    /// <summary>One vehicle's standing and its version, read without tracking, or null when it has none.</summary>
    public async Task<StoredStanding?> ReadStandingAsync(string vehicleId)
    {
        using var db = await factory.CreateDbContextAsync();
        var row = await db.Standings.AsNoTracking().SingleOrDefaultAsync(row => row.VehicleId == vehicleId);
        return row is null ? null : Stored(row);
    }

    /// <summary>
    /// The compare-and-set itself: one UPDATE that names the version it read
    /// and moves it by one. One row changed means this writer won; none means
    /// another writer moved the standing first.
    /// </summary>
    private static async Task<bool> TryMoveStandingAsync(YardDbContext db, string vehicleId, VehicleStanding standing, long expected)
    {
        int moved = await db.Standings
            .Where(row => row.VehicleId == vehicleId && row.Version == expected)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Amount, standing.Amount)
                .SetProperty(row => row.BidCount, standing.BidCount)
                .SetProperty(row => row.HighBidderId, standing.HighBidderId)
                .SetProperty(row => row.SoldBuyNow, standing.SoldBuyNow)
                .SetProperty(row => row.AtMs, standing.AtMs)
                .SetProperty(row => row.Version, expected + 1));
        return moved == 1;
    }

    /// <summary>Whether a standing row exists for the vehicle now, read on a fresh context after a failed insert.</summary>
    private async Task<bool> StandingExistsAsync(string vehicleId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Standings.AsNoTracking().AnyAsync(row => row.VehicleId == vehicleId);
    }

    /// <summary>A new standing row at the given version.</summary>
    private static StandingRow RowOf(string vehicleId, VehicleStanding standing, long version) => new()
    {
        VehicleId = vehicleId,
        Amount = standing.Amount,
        BidCount = standing.BidCount,
        HighBidderId = standing.HighBidderId,
        SoldBuyNow = standing.SoldBuyNow,
        AtMs = standing.AtMs,
        Version = version,
    };

    /// <summary>A row as the port hands it back, the version written as text.</summary>
    private static StoredStanding Stored(StandingRow row) => new(
        row.VehicleId,
        new VehicleStanding(row.Amount, row.BidCount, row.HighBidderId, row.SoldBuyNow, row.AtMs),
        row.Version.ToString(CultureInfo.InvariantCulture));
    // #endregion standing-store
}
