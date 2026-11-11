namespace TheYard.Infrastructure.Shared.History;

/// <summary>
/// The last read of Azure's bill, whole, in one row (ADR: What Azure charges, the addendum on keeping the read). One row
/// only, its id always <see cref="TheRow"/>: a read is thirty-five whole days and a fresh forecast, so the newer read
/// replaces the last, revisions included, exactly as the in-memory copy does.
/// </summary>
public sealed class CostReadRow
{
    /// <summary>The one row's id.</summary>
    public const int TheRow = 1;

    /// <summary>Always <see cref="TheRow"/>.</summary>
    public required int Id { get; set; }

    /// <summary>When Cost Management answered with this read.</summary>
    public required DateTimeOffset ReadAt { get; set; }

    /// <summary>The charges by resource by day, as JSON, oldest first.</summary>
    public required string Days { get; set; }

    /// <summary>The forecast days, as JSON, oldest first.</summary>
    public required string Forecast { get; set; }
}
