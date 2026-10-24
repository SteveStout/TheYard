// The catalogue port's fill on a SQLite file, and the page order a typed word is read in. A file of
// its own because EfVehicleCatalogue.cs holds the queries and the two are read for different reasons
// (ADR: The store does the work).
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>The SQLite file's fill and the word order; the class is described in EfVehicleCatalogue.cs.</summary>
public sealed partial class EfVehicleCatalogue
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
    private async Task FillFileAsync(IReadOnlyList<CatalogueEntry> batch)
    {
        await using var db = await contexts.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
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
            await command.ExecuteNonQueryAsync();
        }
        // Each row's words in the same transaction, so a row is never stored without them.
        await WriteWordsAsync(connection, transaction, batch.SelectMany(entry => CatalogueWords.For(entry.Seq, VehicleSearchIndex.TextFor(entry.Vehicle))));
        await transaction.CommitAsync();
    }

    /// <summary>The same order as <c>Ordered</c>, each key written as "+ 0" so no index serves it and the words index is read alone.</summary>
    private static IQueryable<CatalogueRow> Unindexed(IQueryable<CatalogueRow> rows, CatalogueOrder order) => order switch
    {
        CatalogueOrder.EndAscending => rows.OrderBy(row => row.EndOffsetMs + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.StartAscending => rows.OrderBy(row => row.StartOffsetMs + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.EndDescending => rows.OrderByDescending(row => row.EndOffsetMs + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.PriceAscending => rows.OrderBy(row => row.Price + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.PriceDescending => rows.OrderByDescending(row => row.Price + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.ConditionDescending => rows.OrderByDescending(row => row.ConditionGrade + 0).ThenBy(row => row.Seq + 0),
        CatalogueOrder.BidsDescending => rows.OrderByDescending(row => row.BidCount + 0).ThenBy(row => row.Seq + 0),
        _ => rows.OrderBy(row => row.Seq + 0),
    };
}
