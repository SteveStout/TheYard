// The document shape of the bids container: one buyer's standing on one vehicle, partitioned on
// the buyer. One file per container, so what a container holds is one short file to open; the
// mapping to the domain record is in Documents.cs.
using System.Text.Json.Serialization;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
/// <summary>
/// One buyer's standing on one vehicle. The id is the vehicle id and the
/// partition is the buyer, so the pair that is the primary key on SQL Server is
/// the (partition, id) pair here, and every read and write is a point operation.
/// </summary>
public sealed class BidDocument
{
    /// <summary>The vehicle the standing is on, which is the document id (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The buyer, and the partition key (<c>user_id</c>).</summary>
    public string UserId { get; set; } = "";

    /// <summary>The buyer's highest bid, in whole dollars (<c>amount</c>).</summary>
    public int Amount { get; set; }

    /// <summary>How many bids the buyer has placed on the vehicle (<c>bid_count</c>).</summary>
    public int BidCount { get; set; }

    /// <summary>Whether the buyer bought the vehicle at its buy-now price (<c>won_buy_now</c>).</summary>
    public bool WonBuyNow { get; set; }

    /// <summary>When the last bid was placed, in milliseconds since the epoch, UTC (<c>at_ms</c>).</summary>
    public long AtMs { get; set; }

    /// <summary>
    /// The concurrency token, kept by the store. <c>rowversion</c> on SQL Server,
    /// a token the store moves on SQLite, and the document's own etag here: a
    /// replace that sends a stale one is refused with 412
    /// (ADR: The SQL Server backend).
    /// </summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}
// #endregion documents
