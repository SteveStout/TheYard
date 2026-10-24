// The model for the activity counters (ADR: Site activity, and the line an address does not cross). Its
// own type, called from YardDbContext.OnModelCreating, so the counters' tables change here and nowhere else.
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>The activity counters' part of the relational model.</summary>
public static class ActivityModel
{
    /// <summary>The two activity tables, keyed by their natural keys.</summary>
    /// <param name="model">The model being built.</param>
    internal static void Configure(ModelBuilder model)
    {
        // #region activity-model
        // The activity counters (ADR: Site activity, and the line an address
        // does not cross). Natural keys again: an hour on a store is one row,
        // a visitor token on a store on a day is one row. The counters move by
        // ExecuteUpdate so two containers writing the same Azure SQL row add
        // rather than overwrite; the paths column is a JSON object the store
        // reads, merges and writes back, bounded at twenty entries, and the
        // one place a batch can lose to another batch, which the record says.
        model.Entity<ActivityHourRow>(hour =>
        {
            hour.ToTable("ActivityHours");
            hour.HasKey(row => new { row.Store, row.Hour });
            hour.Property(row => row.Store).HasMaxLength(YardColumns.StoreKeyLength);
            hour.Property(row => row.Paths).HasMaxLength(YardColumns.PathsLength);
        });

        model.Entity<ActivityVisitorRow>(visitor =>
        {
            visitor.ToTable("ActivityVisitors");
            visitor.HasKey(row => new { row.Store, row.Day, row.Visitor });
            visitor.Property(row => row.Store).HasMaxLength(YardColumns.StoreKeyLength);
            // yyyy-MM-dd, and nothing else is ever written here.
            visitor.Property(row => row.Day).HasMaxLength(10);
            // The token: the first 16 bytes of a keyed hash, as 32 hex characters.
            visitor.Property(row => row.Visitor).HasMaxLength(32);
            // Three octets and an x, or the same shape for an address that
            // could not be read: never four octets.
            visitor.Property(row => row.Network).HasMaxLength(YardColumns.NetworkLength);
            visitor.Property(row => row.Paths).HasMaxLength(YardColumns.PathsLength);
            visitor.Property(row => row.Sources).HasMaxLength(YardColumns.PathsLength);
        });
        // #endregion activity-model
    }
}
