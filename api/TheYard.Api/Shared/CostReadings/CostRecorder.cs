// The background service that reads the bill once an hour. Its own file because when to read
// (the hour, the retry, the start delay) is a separate question from how to read, which is
// CostReader.cs, and from what a read leaves behind, which is CostKeeping.cs.

namespace TheYard.Api.Shared.CostReadings;

// #region cost-recorder
/// <summary>
/// Reads Cost Management once an hour and holds the answer (ADR: What Azure
/// charges). Never on a request: the service is rate limited, lags the day by
/// eight to twenty four hours, and a card that asked it on every open would
/// spend the allowance on the same answer.
///
/// <para>This type is the schedule and nothing else: the wait after start, the wait after each
/// answer, and the loop between them. The read itself and everything it leaves behind are
/// <see cref="CostKeeping"/>, which is why this is handed two things rather than six.</para>
/// </summary>
/// <param name="keeping">One read of the bill, kept, with its failures already written down.</param>
/// <param name="firstReadAfter">How long after the start the first read waits (see <see cref="FirstReadAfter"/>).</param>
public sealed class CostRecorder(CostKeeping keeping, TimeSpan firstReadAfter) : BackgroundService
{
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
    /// <param name="defaultStore">The store this process serves by default, or null when it serves both.</param>
    public static TimeSpan FirstReadAfter(string? defaultStore) =>
        string.Equals(defaultStore, StoreKeys.Document, StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(90)
            : TimeSpan.FromSeconds(30);

    /// <summary>One read, public so the suite can run one without waiting an hour; says how it went.</summary>
    /// <param name="cancellation">The read's cancellation.</param>
    public Task<CostOutcome> RecordOnceAsync(CancellationToken cancellation) => keeping.RecordOnceAsync(cancellation);

    /// <summary>
    /// The loop: a short wait after start (see FirstReadAfter), then a read, then a wait set by how the read went.
    /// A run that is not on Azure stops after its first read, since there is nothing to ask with.
    /// </summary>
    /// <param name="stopping">The host's stopping token, which ends the loop.</param>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // A short wait after the start, so the first read never competes with
        // the catalogue this process loads as it starts, and the two sites'
        // first reads land a minute apart.
        await Task.Delay(firstReadAfter, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stopping.IsCancellationRequested)
        {
            CostOutcome outcome;
            try
            {
                outcome = await keeping.TryRecordAsync(stopping);
                if (!keeping.Configured)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }

            await Task.Delay(WaitAfter(outcome, keeping.RetryAfter), stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion cost-recorder
