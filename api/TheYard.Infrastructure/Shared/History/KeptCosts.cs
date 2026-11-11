using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.History;

namespace TheYard.Infrastructure.Shared.History;

/// <summary>
/// The last read of the bill, held in this process and kept in the relational store (ADR: What Azure charges, the
/// addendum on keeping the read). The process answers from its own copy while that copy is under an hour old; past
/// that, or with nothing held, it reads the kept row, which is how a process that has just rolled starts with the last
/// bill. A read is written to both, and a store that
/// is not there yet costs nothing but the copy across a roll.
/// </summary>
/// <param name="contexts">The relational store's quiet context factory, or null while the store is not attached.</param>
/// <param name="clock">The clock the copy's age is read on.</param>
public sealed class KeptCosts(Func<IDbContextFactory<YardDbContext>?> contexts, TimeProvider clock) : ICostHistory
{
    /// <summary>How old this process's copy may be before the kept row is read again: the recorder's hour.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private readonly CostsInMemory _held = new();
    private readonly EfCostStore _store = new(contexts);

    /// <summary>Always available, because this process holds the read whatever the store says; the reason says where it is kept.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<CostHistoryAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        var store = await _store.AvailabilityAsync(cancellation);
        return new CostHistoryAvailability(true, store.Available
            ? "kept in the relational store across rolls, and held in this site's memory between its reads"
            : $"held in this site's memory only: {store.Reason}");
    }

    /// <summary>Holds the read here and keeps it in the store; a store that cannot take it leaves the copy here.</summary>
    /// <param name="days">The charges by resource by day.</param>
    /// <param name="forecast">The forecast days.</param>
    /// <param name="readAt">When Cost Management answered.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task KeepAsync(IReadOnlyList<CostDay> days, IReadOnlyList<CostForecastDay> forecast, DateTimeOffset readAt, CancellationToken cancellation)
    {
        await _held.KeepAsync(days, forecast, readAt, cancellation);
        if ((await _store.AvailabilityAsync(cancellation)).Available)
        {
            try
            {
                await _store.SaveAsync(new CostKept(days, forecast, readAt), cancellation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The copy here still answers; the next read writes the row again.
            }
        }
    }

    /// <summary>Every day held from <paramref name="fromDay"/> on and every forecast day, from the newer of this process's copy and the kept row.</summary>
    /// <param name="fromDay">The first UTC day wanted, as yyyy-MM-dd.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<CostKept> ReadAsync(string fromDay, CancellationToken cancellation)
    {
        var held = await _held.ReadAsync(fromDay, cancellation);
        if (held.ReadAt is { } at && clock.GetUtcNow() - at < Fresh)
        {
            return held;
        }
        if (!(await _store.AvailabilityAsync(cancellation)).Available)
        {
            return held;
        }
        try
        {
            if (await _store.LoadAsync(cancellation) is { ReadAt: { } keptAt } kept && (held.ReadAt is null || keptAt > held.ReadAt))
            {
                await _held.KeepAsync(kept.Days, kept.Forecast, keptAt, cancellation);
                return await _held.ReadAsync(fromDay, cancellation);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A row that cannot be read leaves the copy here to answer.
        }
        return held;
    }
}
