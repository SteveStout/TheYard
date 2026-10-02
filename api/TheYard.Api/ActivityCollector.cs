// The collector: takes each hit off the request path into a bounded channel
// and writes what is queued to the keeper in batches. Its own file because it
// is the one part of the activity feature that runs in the background and
// holds state; Activity.cs lists the other parts.
using System.Threading.Channels;
using TheYard.Application;

namespace TheYard.Api;

// #region collector
/// <summary>
/// Hits off the request path. A request offers its hit to a bounded channel
/// and goes on its way; this service drains the channel every few seconds,
/// or sooner when it fills, and writes everything queued as one batch to the
/// keeper, each hit still naming the store that served it. A full channel
/// drops the oldest hit rather than blocking a request, because a count of
/// visitors is never worth a visitor's time, and the drop is counted so the
/// card can say it happened: the channel's own callback counts each hit it
/// evicts, because with DropOldest the write that caused the eviction still
/// succeeds, so the write alone would never show the loss.
/// </summary>
public sealed class ActivityCollector : BackgroundService
{
    /// <summary>The most hits the channel holds before it drops the oldest.</summary>
    public const int Capacity = 10_000;

    /// <summary>How often the channel is drained when it is not filling: five seconds.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>How many queued hits wake the drain before the interval is up.</summary>
    private const int DrainAt = 500;

    /// <summary>The bounded queue between the request path and the keeper.</summary>
    private readonly Channel<ActivityHit> _channel;

    /// <summary>Every store this container runs, by key.</summary>
    private readonly IReadOnlyDictionary<string, IActivityStore> _stores;

    /// <summary>Where a failed batch is reported, by its count, its store and its exception type.</summary>
    private readonly ILogger<ActivityCollector> _logger;

    /// <summary>Hits offered since the last drain, which decides when to wake it early.</summary>
    private int _pending;

    /// <summary>Hits the channel accepted since this process started.</summary>
    private long _offered;

    /// <summary>Hits the keeper accepted since this process started.</summary>
    private long _written;

    /// <summary>Batches the keeper refused since this process started.</summary>
    private long _failedBatches;

    /// <summary>Hits the full channel evicted since this process started.</summary>
    private long _dropped;

    /// <summary>When a batch last reached the keeper, or null if none has.</summary>
    private DateTimeOffset? _lastWrite;

    /// <summary>
    /// The keeper is the one store every batch goes to, whichever store served
    /// the request; the row keeps the serving store's key as data. Azure
    /// Cosmos DB wherever it is configured, because it never expires the
    /// activity rows (ADR: Site activity, and the line an address does not
    /// cross, addendum of 14 September) and because a serverless relational
    /// database written every five seconds never pauses, which is what spent
    /// the free amount in fourteen days. With no Cosmos DB on the container,
    /// the default store keeps its own rows, which is what the tests run on.
    /// </summary>
    public ActivityCollector(IReadOnlyDictionary<string, IActivityStore> stores, string keeperKey, ILogger<ActivityCollector> logger)
    {
        _stores = stores;
        KeeperKey = keeperKey;
        _logger = logger;
        _channel = Channel.CreateBounded<ActivityHit>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>A collector whose keeper is the first store given, or none when there are no stores.</summary>
    public ActivityCollector(IReadOnlyDictionary<string, IActivityStore> stores, ILogger<ActivityCollector> logger)
        : this(stores, stores.Keys.FirstOrDefault() ?? "none", logger)
    {
    }

    /// <summary>The stores by key, for the report.</summary>
    public IReadOnlyDictionary<string, IActivityStore> Stores => _stores;

    /// <summary>The key of the store every batch is written to and every report is read from.</summary>
    public string KeeperKey { get; }

    /// <summary>The keeper itself, or the null store when the key names nothing.</summary>
    public IActivityStore Keeper => _stores.GetValueOrDefault(KeeperKey) ?? NullActivityStore.Instance;

    /// <summary>What has passed through: offered, written, batches that failed, hits a full channel dropped, and the last time anything was written.</summary>
    public (long Offered, long Written, long FailedBatches, long Dropped, DateTimeOffset? LastWrite) Counters =>
        (Interlocked.Read(ref _offered), Interlocked.Read(ref _written), Interlocked.Read(ref _failedBatches), Interlocked.Read(ref _dropped), _lastWrite);

    /// <summary>Called on the request thread and returns at once.</summary>
    public void Offer(ActivityHit hit)
    {
        if (_channel.Writer.TryWrite(hit))
        {
            Interlocked.Increment(ref _offered);
            if (Interlocked.Increment(ref _pending) >= DrainAt)
            {
                _wake.TrySetResult();
            }
        }
    }

    /// <summary>Completed by an offer that fills the queue to the drain mark, so the loop drains before the interval is up; replaced after each wake.</summary>
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The background loop: wait for the interval or an early wake, drain, and drain once more on the way out.</summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.WhenAny(Task.Delay(Interval, stopping), _wake.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await DrainAsync(CancellationToken.None);
        }

        // Whatever is left goes out with the process.
        await DrainAsync(CancellationToken.None);
    }

    /// <summary>One pass: everything queued right now, one batch, to the keeper; each hit still names the store that served it.</summary>
    public async Task DrainAsync(CancellationToken cancellation)
    {
        var batch = new List<ActivityHit>();
        while (_channel.Reader.TryRead(out var hit))
        {
            batch.Add(hit);
        }

        Interlocked.Exchange(ref _pending, 0);
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await Keeper.RecordAsync(batch, cancellation);
            Interlocked.Add(ref _written, batch.Count);
            _lastWrite = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedBatches);
            // The type, never the message: a store's message can name a server.
            _logger.LogWarning("An activity batch of {Count} hits was dropped by {Store}: {Type}", batch.Count, KeeperKey, ex.GetType().Name);
        }
    }
}
// #endregion collector
