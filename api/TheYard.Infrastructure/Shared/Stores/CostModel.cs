// The model for the kept bill (ADR: What Azure charges, the addendum on keeping the read). Its own type, called from
// YardDbContext.OnModelCreating, so the kept read's table changes here and nowhere else.
using Microsoft.EntityFrameworkCore;
using TheYard.Infrastructure.Shared.History;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>The kept bill's part of the relational model.</summary>
public static class CostModel
{
    /// <summary>The one-row table the last read of the bill is kept in.</summary>
    /// <param name="model">The model being built.</param>
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<CostReadRow>(read =>
        {
            read.ToTable("CostReads");
            read.HasKey(row => row.Id);
            // The id is the one row's, never generated.
            read.Property(row => row.Id).ValueGeneratedNever();
        });
    }
}
