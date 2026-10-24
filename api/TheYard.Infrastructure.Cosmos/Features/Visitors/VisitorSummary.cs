// The running count of scanners the visitors' partition has ever kept: its own file because it is its own
// document, beside the visitor records in the activity container (CosmosVisitorStore.cs).
namespace TheYard.Infrastructure.Cosmos.Features.Visitors;

/// <summary>
/// The running count of scanners ever kept, one document in the visitors' partition: the scanner records expire
/// after ninety days, and "left out of the count" keeps its history here (ADR-116).
/// </summary>
public sealed class VisitorSummary
{
    /// <summary>The summary's id and kind.</summary>
    public const string Name = "visitors-summary";

    /// <summary>Always <see cref="Name"/> (<c>id</c>).</summary>
    public string Id { get; set; } = Name;

    /// <summary>Always <see cref="Name"/> (<c>kind</c>).</summary>
    public string Kind { get; set; } = Name;

    /// <summary>The partition, always <see cref="CosmosVisitorStore.Partition"/> (<c>day</c>).</summary>
    public string Day { get; set; } = CosmosVisitorStore.Partition;

    /// <summary>How many scanners have ever been kept (<c>scanners_ever</c>).</summary>
    public int ScannersEver { get; set; }
}
