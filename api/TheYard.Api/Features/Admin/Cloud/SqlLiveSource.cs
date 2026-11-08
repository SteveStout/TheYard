// The Azure SQL card's live source (ADR: Live dials where the source allows, the addendum on Azure SQL's line): the
// database's own view of itself, sys.dm_db_resource_stats, which it keeps at fifteen-second intervals for an hour and
// which costs nothing to read, asked every fifteen seconds while the card is open. "DTU, right now" is the greatest of
// the processor, data IO and log write shares of the newest interval, the formula Microsoft's documentation gives.
using TheYard.Application.Shared.Live;

namespace TheYard.Api.Features.Admin.Cloud;

// #region sql-live-source
/// <summary>Azure SQL's own fifteen-second view, every fifteen seconds while leased.</summary>
/// <param name="read">One read of the view's newest rows.</param>
/// <param name="hasStore">Whether this process keeps a relational store at all.</param>
public sealed class SqlLiveSource(Func<CancellationToken, Task<StoreLoad>> read, Func<bool> hasStore) : ILiveSource
{
    /// <summary>How many of the view's rows a read asks for: ten minutes of them.</summary>
    public const int Rows = 40;

    private DateTime? _last;

    /// <inheritdoc />
    public string Key => "sqldb";

    /// <inheritdoc />
    public int GrainSeconds => 15;

    /// <inheritdoc />
    public string Source => "the database's own view, 15 s, Microsoft's example formula";

    /// <inheritdoc />
    public string? Absent => hasStore() ? null : "this site keeps no relational store";

    /// <summary>The newest interval as one sample, or null when the view has nothing newer than the last read.</summary>
    public async Task<LiveSample?> SampleAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        var load = await read(cancellation);
        if (!load.Available || load.Rows.Count == 0)
        {
            return null;
        }
        var newest = load.Rows.MaxBy(row => row.At)!;
        if (_last is { } last && newest.At <= last)
        {
            return null;
        }
        _last = newest.At;
        return new LiveSample(new DateTimeOffset(DateTime.SpecifyKind(newest.At, DateTimeKind.Utc)), new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["dtu"] = Dtu(newest),
            ["cpu"] = newest.CpuPercent,
            ["data_io"] = newest.DataIoPercent,
            ["log_write"] = newest.LogWritePercent,
        });
    }

    /// <summary>DTU, right now: the greatest of the processor, data IO and log write shares.</summary>
    public static double Dtu(ResourceStatRow row) => Math.Max(row.CpuPercent, Math.Max(row.DataIoPercent, row.LogWritePercent));
}
// #endregion sql-live-source
