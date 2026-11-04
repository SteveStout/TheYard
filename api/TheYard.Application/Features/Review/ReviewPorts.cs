// The review route's reach (ADR: Ten minutes, seven stops, the addendum on the reach rows): how far a
// visitor got along the seven stops, one row a visitor a day under the stable keyed hash ADR-116 already
// makes, with the tagged code the page opened on. The shapes, the window and the two ports.
namespace TheYard.Application.Features.Review;

// #region review-ports
/// <summary>One report from the page: a visitor reached a stop (0 is opened the route), on a day, maybe through a code.</summary>
/// <param name="Hash">The visitor's stable keyed hash (VisitorTokens.PersonFor); never an address or an agent.</param>
/// <param name="Day">The UTC day as yyyy-MM-dd.</param>
/// <param name="Stop">0 for opened, 1 to 7 for the stop reached.</param>
/// <param name="Code">The six-character code the page opened on, or null.</param>
/// <param name="At">When the report arrived.</param>
public sealed record ReviewReach(string Hash, string Day, int Stop, string? Code, DateTimeOffset At);

/// <summary>One visitor's day on the route, folded: the highest stop, the first code, the first and the last report.</summary>
/// <param name="Hash">The visitor's stable keyed hash.</param>
/// <param name="Day">The UTC day as yyyy-MM-dd.</param>
/// <param name="Stop">The highest stop reached that day; 0 is opened.</param>
/// <param name="Code">The code kept for the day, the first one seen, or null.</param>
/// <param name="First">The first report that day.</param>
/// <param name="Last">The last report that day.</param>
public sealed record ReviewRow(string Hash, string Day, int Stop, string? Code, DateTimeOffset First, DateTimeOffset Last);

/// <summary>One tagged code's row on the card: how far its visitors got, how long it took, and the last day it was seen.</summary>
/// <param name="Code">The code; never a name.</param>
/// <param name="Stop">The highest stop reached through it; 0 is opened and left.</param>
/// <param name="Minutes">Whole minutes from its first report to its last.</param>
/// <param name="LastDay">The last UTC day it reported, yyyy-MM-dd.</param>
public sealed record ReviewCodeRow(string Code, int Stop, int Minutes, string LastDay);

/// <summary>The card's report: the visitors who opened the route and how many reached each stop, by code too.</summary>
/// <param name="Available">True when the reach is kept and was read.</param>
/// <param name="Window">The window read, as its name: 24h, 7d, 30d or 90d.</param>
/// <param name="Opened">The visitors who opened the route in the window, each once.</param>
/// <param name="ReachedByStop">Eight counts: opened, then stops 1 to 7, each the visitors whose highest stop is at least it.</param>
/// <param name="ByCode">The visitors who came through a code, a row a code, newest last day first.</param>
/// <param name="CountedFrom">The first day any reach is kept, yyyy-MM-dd, or null when none is.</param>
/// <param name="Source">Where the reach is kept, in words.</param>
public sealed record ReviewFunnel(bool Available, string Window, int Opened, IReadOnlyList<int> ReachedByStop, IReadOnlyList<ReviewCodeRow> ByCode, string? CountedFrom, string Source);

/// <summary>The windows the card offers: a day, a week, a month and a quarter, spelled the way the links card spells its windows.</summary>
public enum ReviewWindow
{
    /// <summary>The last 24 hours, 24h.</summary>
    Day,

    /// <summary>The last 7 days, 7d.</summary>
    Week,

    /// <summary>The last 30 days, 30d.</summary>
    Month,

    /// <summary>The last 90 days, 90d, the window the links card and the outreach report read.</summary>
    Quarter,
}

/// <summary>A window's name and its length, both ways, so the route and the handler read one spelling.</summary>
public static class ReviewWindows
{
    /// <summary>The windows by name, in the order the card's pills show them.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewWindow> ByName = new Dictionary<string, ReviewWindow>(StringComparer.Ordinal)
    {
        ["24h"] = ReviewWindow.Day,
        ["7d"] = ReviewWindow.Week,
        ["30d"] = ReviewWindow.Month,
        ["90d"] = ReviewWindow.Quarter,
    };

    /// <summary>The window a name asks for; anything else is the week.</summary>
    /// <param name="name">24h, 7d, 30d or 90d, or none.</param>
    public static ReviewWindow Parse(string? name) =>
        name is not null && ByName.TryGetValue(name, out var window) ? window : ReviewWindow.Week;

    /// <summary>The window's name.</summary>
    /// <param name="window">The window.</param>
    public static string NameOf(ReviewWindow window) => ByName.Single(pair => pair.Value == window).Key;

    /// <summary>How far back the window reaches from now.</summary>
    /// <param name="window">The window.</param>
    public static TimeSpan LengthOf(ReviewWindow window) => window switch
    {
        ReviewWindow.Day => TimeSpan.FromHours(24),
        ReviewWindow.Week => TimeSpan.FromDays(7),
        ReviewWindow.Month => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(90),
    };
}

/// <summary>Port: where the page's reports are handed, at once and without waiting; the slice's collector is behind it.</summary>
public interface IReviewReachOffers
{
    /// <summary>Takes one reach and returns at once.</summary>
    /// <param name="reach">The reach.</param>
    void Offer(ReviewReach reach);
}

/// <summary>The read side of the reach (ADR-115): where it is kept, whether it can be read, and the rows since a day.</summary>
public interface IReviewReader
{
    /// <summary>Where the reach is kept, in words, for the card.</summary>
    string Source { get; }

    /// <summary>Whether the reach is kept here and can be read.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<bool> AvailableAsync(CancellationToken cancellation);

    /// <summary>Every row from a UTC day on, a visitor a day.</summary>
    /// <param name="day">The first day, yyyy-MM-dd.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<IReadOnlyList<ReviewRow>> RowsSinceAsync(string day, CancellationToken cancellation);

    /// <summary>The first day any row is kept, or null when none is: the day the card says it started counting.</summary>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<string?> FirstDayAsync(CancellationToken cancellation);
}

/// <summary>Port: where the reach is kept. Writes arrive folded, a row a visitor a day, never from a request thread.</summary>
public interface IReviewStore : IReviewReader
{
    /// <summary>Folds the rows into what is kept: a higher stop replaces a lower one, the first time and the first code stay.</summary>
    /// <param name="rows">The rows the collector folded since its last write.</param>
    /// <param name="cancellation">The write's cancellation.</param>
    Task KeepAsync(IReadOnlyList<ReviewRow> rows, CancellationToken cancellation);
}

/// <summary>A keeper for a host that is told not to keep the reach, saying why: every read is empty, every write dropped.</summary>
/// <param name="why">Why nothing is kept, in one sentence, for the card.</param>
public sealed class NoReviewStore(string why) : IReviewStore
{
    /// <summary>Why nothing is kept.</summary>
    public string Source => why;

    /// <summary>Never: nothing is kept.</summary>
    /// <param name="cancellation">Unused.</param>
    public Task<bool> AvailableAsync(CancellationToken cancellation) => Task.FromResult(false);

    /// <summary>None.</summary>
    /// <param name="day">Unused.</param>
    /// <param name="cancellation">Unused.</param>
    public Task<IReadOnlyList<ReviewRow>> RowsSinceAsync(string day, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<ReviewRow>>([]);

    /// <summary>None.</summary>
    /// <param name="cancellation">Unused.</param>
    public Task<string?> FirstDayAsync(CancellationToken cancellation) => Task.FromResult<string?>(null);

    /// <summary>Drops the rows.</summary>
    /// <param name="rows">Unused.</param>
    /// <param name="cancellation">Unused.</param>
    public Task KeepAsync(IReadOnlyList<ReviewRow> rows, CancellationToken cancellation) => Task.CompletedTask;
}
// #endregion review-ports
