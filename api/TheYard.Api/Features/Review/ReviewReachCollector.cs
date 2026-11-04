// The review route's reports, offered on the request path and written off it once a minute, the way the
// link opens are (LinkOpenCollector.cs): a bounded channel and one keeper. A drain folds what it took into
// a row a visitor a day by the one rule before it writes, so a visitor who walks all seven stops in a
// minute costs one write, not eight.
using System.Threading.Channels;
using TheYard.Application.Features.Review;

namespace TheYard.Api.Features.Review;

// #region review-reach-collector
/// <summary>Takes reaches from the request path and writes them, folded, to the keeper once a minute.</summary>
public sealed class ReviewReachCollector : BackgroundService, IReviewReachOffers
{
    /// <summary>The most reaches the channel holds before it drops the oldest.</summary>
    public const int Capacity = 2_000;

    /// <summary>How often the channel is drained: once a minute.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly Channel<ReviewReach> _channel;
    private readonly IReviewStore _keeper;
    private readonly ILogger<ReviewReachCollector> _logger;
    private long _dropped;

    /// <summary>A collector over one keeper: the document store where it is configured, this process's memory otherwise.</summary>
    /// <param name="keeper">Where the reach is kept.</param>
    /// <param name="logger">Where a dropped batch is reported, by its count and exception type.</param>
    public ReviewReachCollector(IReviewStore keeper, ILogger<ReviewReachCollector> logger)
    {
        _keeper = keeper;
        _logger = logger;
        _channel = Channel.CreateBounded<ReviewReach>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Called on the request thread and returns at once.</summary>
    /// <param name="reach">The reach.</param>
    public void Offer(ReviewReach reach) => _channel.Writer.TryWrite(reach);

    /// <summary>Drains once a minute until the host stops, and once more as it stops.</summary>
    /// <param name="stopping">The host's stopping signal.</param>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await DrainAsync(CancellationToken.None);
        }
        await DrainAsync(CancellationToken.None);
    }

    /// <summary>One pass: everything queued right now, folded a visitor a day, to the keeper.</summary>
    /// <param name="cancellation">The drain's cancellation.</param>
    public async Task DrainAsync(CancellationToken cancellation)
    {
        var batch = new List<ReviewReach>();
        while (_channel.Reader.TryRead(out var reach))
        {
            batch.Add(reach);
        }
        if (Interlocked.Exchange(ref _dropped, 0) is var dropped and > 0)
        {
            _logger.LogWarning("{Count} review reaches were dropped by a full channel", dropped);
        }
        if (batch.Count == 0)
        {
            return;
        }
        try
        {
            await _keeper.KeepAsync(ReviewFolding.Fold(batch), cancellation);
        }
        catch (Exception ex)
        {
            // The type, never the message: a store's message can name a server.
            _logger.LogWarning("A batch of {Count} review reaches was dropped: {Type}", batch.Count, ex.GetType().Name);
        }
    }
}
// #endregion review-reach-collector
