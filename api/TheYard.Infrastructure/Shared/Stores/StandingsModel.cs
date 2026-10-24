// The model for the Standings table: one row per vehicle that the bid path reads and writes with a
// compare-and-set. A file of its own because YardDbContext.cs holds the rest of the model and is
// at the length a reader can take in at once.
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>The Standings table's part of the relational model.</summary>
public static class StandingsModel
{
    // #region standings-model
    /// <summary>
    /// One standing per vehicle, keyed by the vehicle, with a plain version
    /// column the store moves itself. No foreign key on the high bidder: a
    /// deleted account takes its bid rows with it by cascade, and the bid path
    /// reads the rows again whenever a standing is missing, so a standing is
    /// never the only record of a bid.
    /// </summary>
    internal static void ConfigureStandings(ModelBuilder model) =>
        model.Entity<StandingRow>(standing =>
        {
            standing.ToTable("Standings");
            standing.HasKey(row => row.VehicleId);
            standing.Property(row => row.VehicleId).HasMaxLength(YardColumns.IdLength);
            standing.Property(row => row.HighBidderId).HasMaxLength(YardColumns.IdentityKeyLength);
        });
    // #endregion standings-model
}
