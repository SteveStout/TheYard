// The catalogue's fill: a SQLite file's in one prepared statement, Azure SQL Database's through the
// model. Its own type, beside EfVehicleCatalogue, because writing the table changes for reasons the
// queries never do (ADR: The store does the work).
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>The relational catalogue's fill, each row with its words beside it.</summary>
public static class EfCatalogueFill
{
    /// <summary>The Catalogue table's columns, in the order the file fill writes them.</summary>
    private static readonly string[] Columns =
    [
        "Id", "Seq", "Vin", "Year", "Make", "Model", "Trim", "BodyStyle", "ExteriorColor", "InteriorColor",
        "Engine", "Transmission", "Drivetrain", "OdometerKm", "FuelType", "ConditionGrade", "ConditionReport",
        "DamageNotes", "TitleStatus", "Province", "City", "AuctionStart", "StartingBid", "ReservePrice",
        "BuyNowPrice", "Images", "SellingDealership", "Lot", "CurrentBid", "BidCount", "StartOffsetMs",
        "EndOffsetMs", "Price", "SearchText",
    ];

    /// <summary>
    /// A SQLite file's fill: one transaction and one prepared INSERT OR IGNORE run once a row, each row's words beside it,
    /// which writes a hundred thousand rows in seconds where change tracking takes minutes. Only
    /// for a file this process owns; Azure SQL Database is filled through the model, at its pace.
    /// </summary>
    /// <param name="contexts">A context per operation.</param>
    /// <param name="batch">The rows to store.</param>
    internal static async Task FillFileAsync(IDbContextFactory<YardDbContext> contexts, IReadOnlyList<CatalogueEntry> batch, CancellationToken cancellation)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellation);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT OR IGNORE INTO \"Catalogue\" ({string.Join(", ", Columns.Select(c => $"\"{c}\""))}) "
            + $"VALUES ({string.Join(", ", Columns.Select(c => "$" + c))})";
        var parameters = Columns.Select(name =>
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$" + name;
            command.Parameters.Add(parameter);
            return parameter;
        }).ToArray();
        foreach (var entry in batch)
        {
            var r = entry.ToCatalogueRow();
            object?[] values =
            [
                r.Id, r.Seq, r.Vin, r.Year, r.Make, r.Model, r.Trim, r.BodyStyle, r.ExteriorColor, r.InteriorColor,
                r.Engine, r.Transmission, r.Drivetrain, r.OdometerKm, r.FuelType, r.ConditionGrade, r.ConditionReport,
                System.Text.Json.JsonSerializer.Serialize(r.DamageNotes), r.TitleStatus, r.Province, r.City, r.AuctionStart,
                r.StartingBid, r.ReservePrice, r.BuyNowPrice, System.Text.Json.JsonSerializer.Serialize(r.Images),
                r.SellingDealership, r.Lot, r.CurrentBid, r.BidCount, r.StartOffsetMs, r.EndOffsetMs, r.Price, r.SearchText,
            ];
            for (int i = 0; i < values.Length; i++)
            {
                parameters[i].Value = values[i] ?? DBNull.Value;
            }
            await command.ExecuteNonQueryAsync(cancellation);
        }
        // Each row's words in the same transaction, so a row is never stored without them.
        await EfCatalogueWords.WriteWordsAsync(connection, transaction, batch.SelectMany(entry => CatalogueWords.For(entry.Seq, VehicleSearchIndex.TextFor(entry.Vehicle))), cancellation);
        await transaction.CommitAsync(cancellation);
    }

    /// <summary>Azure SQL Database's fill: the batch's rows the table does not already hold, and their words, in one save.</summary>
    /// <param name="contexts">A context per operation.</param>
    /// <param name="batch">The rows to store.</param>
    internal static async Task FillModelAsync(IDbContextFactory<YardDbContext> contexts, IReadOnlyList<CatalogueEntry> batch, CancellationToken cancellation)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellation);
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        int low = batch.Min(entry => entry.Seq);
        int high = batch.Max(entry => entry.Seq);
        var held = (await db.Catalogue.AsNoTracking()
            .Where(row => row.Seq >= low && row.Seq <= high)
            .Select(row => row.Seq)
            .ToListAsync(cancellation)).ToHashSet();
        var rows = batch.Where(entry => !held.Contains(entry.Seq)).Select(entry => entry.ToCatalogueRow()).ToList();
        db.Catalogue.AddRange(rows);
        // Each row's words in the same save, so a row is never stored without them.
        db.CatalogueWords.AddRange(rows.SelectMany(row => CatalogueWords.For(row.Seq, row.SearchText)));
        await db.SaveChangesAsync(cancellation);
    }
}
