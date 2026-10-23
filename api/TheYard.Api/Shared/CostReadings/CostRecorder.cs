// The background service that reads the bill once an hour and keeps the answer for the cards.
// Its own file because when to read (the hour, the retry, the start delay) is a separate
// question from how to read, which is CostReader.cs.
using TheYard.Application;

namespace TheYard.Api.Shared.CostReadings;

// #region cost-recorder
/// <summary>
/// Reads Cost Management once an hour and holds the answer (ADR: What Azure
/// charges). Never on a request: the service is rate limited, lags the day by
/// eight to twenty four hours, and a card that asked it on every open would
/// spend the allowance on the same answer. Thirty-five days each time, the
/// month window and five more, because a day is revised after it ends and the
/// next read is how the revision lands.
/// </summary>
public sealed class CostRecorder(
    CostReader reader,
    ICostHistory history,
    CostStatus status,
    TimeProvider clock,
    ILogger<CostRecorder> logger,
    TimeSpan firstReadAfter) : BackgroundService
{
    /// <summary>The wait the last read asked for, so the loop sleeps as long as the refusal named.</summary>
    private TimeSpan? _retryAfter;

    /// <summary>How often the bill is read.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>How soon a read that did not finish is tried again.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);

    /// <summary>The least a request to slow down is waited, whatever it named.</summary>
    public static readonly TimeSpan ShortestSlowDown = TimeSpan.FromMinutes(1);

    // #region cost-retry
    /// <summary>
    /// How long to wait after a read. An hour after one that went through,
    /// and after one Azure refused for want of a role, which does not appear
    /// in five minutes. Five minutes after one that did not finish, because a
    /// timeout says nothing about the next answer, and an hour of an empty card
    /// for one slow answer is the wrong trade. A request to slow down is
    /// waited exactly as long as Azure named, plus half a minute, between one
    /// minute and an hour; when it named nothing, an hour
    /// (ADR: What Azure charges, addenda).
    /// </summary>
    public static TimeSpan WaitAfter(CostOutcome outcome, TimeSpan? retryAfter = null) => outcome switch
    {
        CostOutcome.Failed => Retry,
        CostOutcome.Refused when retryAfter is { } named =>
            TimeSpan.FromTicks(Math.Clamp((named + TimeSpan.FromSeconds(30)).Ticks, ShortestSlowDown.Ticks, Every.Ticks)),
        _ => Every,
    };
    // #endregion cost-retry

    /// <summary>
    /// How long after the start the first read waits. Where each site has a process of its own and
    /// both roll together, the one whose default store is the document store waits a minute longer,
    /// so the two first reads never reach Cost Management in the same second; the one web app that
    /// serves both sites sets no default store and waits the shorter time.
    /// </summary>
    public static TimeSpan FirstReadAfter(string? defaultStore) =>
        string.Equals(defaultStore, StoreKeys.Document, StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(90)
            : TimeSpan.FromSeconds(30);

    /// <summary>One read, public so the suite can run one without waiting an hour; says how it went.</summary>
    public async Task<CostOutcome> RecordOnceAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (!reader.Configured)
        {
            status.Set(now, false, CostReader.NotConfigured);
            return CostOutcome.Failed;
        }

        var availability = await history.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            status.Set(now, false, availability.Reason);
            return CostOutcome.Failed;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var read = await reader.ReadAsync(today.AddDays(-(CostWindows.ReadDays - 1)), today, cancellation);
        if (read.Outcome == CostOutcome.Read)
        {
            await history.KeepAsync(read.Days, read.Forecast, cancellation);
        }

        status.Set(now, read.Outcome == CostOutcome.Read, read.Note);
        _retryAfter = read.RetryAfter;
        return read.Outcome;
    }

    /// <summary>
    /// The loop: a short wait after start (see FirstReadAfter), then a read, then a wait set by how the read went.
    /// A run that is not on Azure stops after its first read, since there is nothing to ask with.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // A short wait after the start, so the first read never competes with
        // the catalogue this process loads as it starts, and the two sites'
        // first reads land a minute apart.
        await Task.Delay(firstReadAfter, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stopping.IsCancellationRequested)
        {
            var outcome = CostOutcome.Failed;
            try
            {
                outcome = await RecordOnceAsync(stopping);
                if (!reader.Configured)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The type, never the message: a message from the identity
                // endpoint or the service names the resource it refused.
                logger.LogWarning("The bill could not be read ({Exception})", ex.GetType().Name);
                status.Set(clock.GetUtcNow(), false, $"the last read did not finish ({ex.GetType().Name}); the next is in five minutes");
            }

            await Task.Delay(WaitAfter(outcome, _retryAfter), stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion cost-recorder
