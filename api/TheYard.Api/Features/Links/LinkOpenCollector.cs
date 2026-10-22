// Opens of tagged links, offered on the request path and written in batches off it, the way the
// activity hits are (ActivityCollector.cs): a bounded channel, a drain every few seconds or when
// the channel fills, and one keeper. Smaller than the activity collector because an open is rare:
// the operator sends a few dozen links a week. At the host's root beside the activity collector, a
// running thing with its dependencies in its constructor; the request recorder, which is shared,
// offers to it through the Application port ILinkOpenOffers, so the shared code names no class here.
using System.Threading.Channels;
using TheYard.Application.Features.Links;

namespace TheYard.Api.Features.Links;

// #region link-open-collector
/// <summary>Takes opens from the request path and writes them to the keeper in batches, never from a request thread.</summary>
public sealed class LinkOpenCollector : BackgroundService, ILinkOpenOffers
{
    /// <summary>The most opens the channel holds before it drops the oldest.</summary>
    public const int Capacity = 2_000;

    /// <summary>How often the channel is drained when it is not filling: five seconds.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>How many queued opens wake the drain before the interval is up.</summary>
    private const int DrainAt = 100;

    private readonly Channel<LinkOpen> _channel;
    private readonly ILinkOpens _keeper;
    private readonly ILogger<LinkOpenCollector> _logger;
    private int _pending;
    private long _dropped;
    private volatile TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A collector over one keeper: the document store where it is configured, this process's memory otherwise.</summary>
    /// <param name="keeper">Where opens are kept.</param>
    /// <param name="logger">Where a dropped batch is reported, by its count and exception type.</param>
    public LinkOpenCollector(ILinkOpens keeper, ILogger<LinkOpenCollector> logger)
    {
        _keeper = keeper;
        _logger = logger;
        _channel = Channel.CreateBounded<LinkOpen>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Called on the request thread and returns at once.</summary>
    /// <param name="open">The open.</param>
    public void Offer(LinkOpen open)
    {
        if (_channel.Writer.TryWrite(open))
        {
            if (Interlocked.Increment(ref _pending) >= DrainAt)
            {
                _wake.TrySetResult();
            }
        }
    }

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
        await DrainAsync(CancellationToken.None);
    }

    /// <summary>One pass: everything queued right now, one batch, to the keeper.</summary>
    /// <param name="cancellation">The drain's cancellation.</param>
    public async Task DrainAsync(CancellationToken cancellation)
    {
        var batch = new List<LinkOpen>();
        while (_channel.Reader.TryRead(out var open))
        {
            batch.Add(open);
        }
        Interlocked.Exchange(ref _pending, 0);
        // A full channel drops its oldest; said once a drain, by count, so a flood is one line.
        if (Interlocked.Exchange(ref _dropped, 0) is var dropped and > 0)
        {
            _logger.LogWarning("{Count} link opens were dropped by a full channel", dropped);
        }
        if (batch.Count == 0)
        {
            return;
        }
        try
        {
            await _keeper.RecordAsync(batch, cancellation);
        }
        catch (Exception ex)
        {
            // The type, never the message: a store's message can name a server.
            _logger.LogWarning("A batch of {Count} link opens was dropped: {Type}", batch.Count, ex.GetType().Name);
        }
    }
}
// #endregion link-open-collector
