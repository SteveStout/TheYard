// The model for the Catalogue table: the hundred thousand vehicles the store filters, orders and
// pages. A file of its own because YardDbContext.cs holds the rest of the model and is at the length
// a reader can take in at once (ADR: The store does the work).
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure;

/// <summary>The catalogue's part of the model; the context itself is described in YardDbContext.cs.</summary>
public sealed partial class YardDbContext
{
    /// <summary>All hundred thousand vehicles with their slot, read a page at a time.</summary>
    public DbSet<CatalogueRow> Catalogue => Set<CatalogueRow>();

    /// <summary>Every word of every vehicle's searchable text, one row each, what a typed word is sought in.</summary>
    public DbSet<CatalogueWordRow> CatalogueWords => Set<CatalogueWordRow>();

    // #region catalogue-model
    /// <summary>
    /// The catalogue at the seed table's widths, keyed by id, unique on its place, one index
    /// per order a page is read in, each ending in the place so a tie reads the same on every
    /// store, and one narrow index holding every column a count compares. Which index is clustered, and which keys descend, is the SQL project's to say.
    /// </summary>
    private static void ConfigureCatalogue(ModelBuilder model, bool sqlServer) =>
        model.Entity<CatalogueRow>(row =>
        {
            row.ToTable("Catalogue");
            row.HasKey(r => r.Id);
            row.Property(r => r.Seq).IsRequired();
            row.HasIndex(r => r.Seq).IsUnique();
            row.HasIndex(r => new { r.EndOffsetMs, r.Seq, r.StartOffsetMs });
            row.HasIndex(r => new { r.StartOffsetMs, r.Seq, r.EndOffsetMs });
            row.HasIndex(r => new { r.Price, r.Seq, r.StartOffsetMs, r.EndOffsetMs });
            // A descending order has an index that descends on its figure and ascends on the
            // place, the way the page is ordered, so a page reads the index in order and stops; an
            // index the wrong way round on either key has the planner sort every match instead.
            row.HasIndex(r => new { r.Price, r.Seq, r.StartOffsetMs, r.EndOffsetMs }, "IX_Catalogue_PriceDescending")
                .IsDescending(true, false, false, false);
            row.HasIndex(r => new { r.ConditionGrade, r.Seq, r.StartOffsetMs, r.EndOffsetMs }).IsDescending(true, false, false, false);
            row.HasIndex(r => new { r.BidCount, r.Seq, r.StartOffsetMs, r.EndOffsetMs }).IsDescending(true, false, false, false);
            // The schedule indexes carry the other end of the window, and the price and grade
            // indexes carry both ends, so a segment's count, a range's count and a live page's test
            // of its start are read off the index, never the row. The counts
            // index holds every column a count compares, so a filtered count reads an index a few
            // dozen bytes a row wide rather than the table's two kilobytes a row; it leads with the
            // columns a page never ranges over, so no page is planned off it and then sorted.
            row.HasIndex(r => new { r.Province, r.TitleStatus, r.BodyStyle, r.Make, r.ConditionGrade, r.Price, r.StartOffsetMs, r.EndOffsetMs })
                .HasDatabaseName("IX_Catalogue_Counts");
            // The words: the searchable text with every column a word's count or page compares or
            // orders by, so a typed word is looked for in an index a third of the row's width.
            row.HasIndex(r => new { r.SearchText, r.StartOffsetMs, r.EndOffsetMs, r.Price, r.ConditionGrade, r.BidCount, r.Seq })
                .HasDatabaseName("IX_Catalogue_Words");

            row.Property(r => r.Id).HasMaxLength(IdLength);
            row.Property(r => r.Vin).HasMaxLength(17).IsUnicode(false);
            row.Property(r => r.Make).HasMaxLength(64);
            row.Property(r => r.Model).HasMaxLength(64);
            row.Property(r => r.Trim).HasMaxLength(64);
            row.Property(r => r.BodyStyle).HasMaxLength(32);
            row.Property(r => r.ExteriorColor).HasMaxLength(32);
            row.Property(r => r.InteriorColor).HasMaxLength(32);
            row.Property(r => r.Engine).HasMaxLength(128);
            row.Property(r => r.Transmission).HasMaxLength(64);
            row.Property(r => r.Drivetrain).HasMaxLength(16);
            row.Property(r => r.FuelType).HasMaxLength(32);
            row.Property(r => r.ConditionReport).HasMaxLength(1024);
            row.Property(r => r.TitleStatus).HasMaxLength(32);
            row.Property(r => r.Province).HasMaxLength(64);
            row.Property(r => r.City).HasMaxLength(64);
            row.Property(r => r.SellingDealership).HasMaxLength(128);
            row.Property(r => r.Lot).HasMaxLength(32);
            row.Property(r => r.SearchText).HasMaxLength(CatalogueRows.SearchTextLength);

            if (sqlServer)
            {
                // The seed table's two SQL Server types, for the seed table's reasons.
                row.Property(r => r.ConditionGrade).HasConversion<decimal>().HasPrecision(3, 1);
                row.Property(r => r.AuctionStart)
                    .HasConversion(AuctionStartToDateTime)
                    .HasColumnType("datetime2(0)");
            }
        });
    // #endregion catalogue-model

    /// <summary>
    /// The words, keyed by the place and the word so the fill's check for a vehicle's words is a
    /// seek, and indexed by the word so a typed word is a range that hands back places.
    /// </summary>
    private static void ConfigureCatalogueWords(ModelBuilder model) =>
        model.Entity<CatalogueWordRow>(row =>
        {
            row.ToTable("CatalogueWords");
            row.HasKey(r => new { r.Seq, r.Word });
            row.HasIndex(r => new { r.Word, r.Seq }).HasDatabaseName("IX_CatalogueWords_Word");
            row.Property(r => r.Word).HasMaxLength(TheYard.Infrastructure.CatalogueWords.WordLength);
        });
}
