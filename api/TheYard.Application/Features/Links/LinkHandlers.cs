// The Links slice's three handlers (ADR: Tagged links, who opened what; ADR: The onion cut into
// vertical slices, rule 7): where a pass-through link goes, what the Admin tab's card reads, and
// the open the page reports for itself. Each takes its port by constructor and is tested with a
// fake and no host.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Links;

// #region link-targets
/// <summary>Port: where each pass-through link leads, by its key, from the operator's configuration.</summary>
public interface ILinkTargets
{
    /// <summary>The address the key leads to, or null when no link has the key.</summary>
    /// <param name="key">github, linkedin, or anything else the configuration names.</param>
    Uri? For(string key);

    /// <summary>Every key the configuration names, for the card.</summary>
    IReadOnlyList<string> Keys { get; }
}

/// <summary>Where a pass-through link goes: the configured address for its key, or nowhere.</summary>
/// <param name="targets">The configured addresses.</param>
public sealed class GoHandler(ILinkTargets targets) : IQueryHandler<GoQuery, Uri?>
{
    /// <summary>The address to send the visitor on to, or null for a key the configuration does not name.</summary>
    /// <param name="query">The key from the route.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Uri?> HandleAsync(GoQuery query, CancellationToken cancellation) =>
        Task.FromResult(targets.For(query.Key.Trim().ToLowerInvariant()));
}

/// <summary>Where a pass-through link leads. The open itself is recorded by the pipeline, so this only reads.</summary>
/// <param name="Key">The key from the route.</param>
public sealed record GoQuery(string Key);
// #endregion link-targets

// #region read-link-opens
/// <summary>One code's opens over the window, as the card shows them: codes, never names.</summary>
/// <param name="Code">The code.</param>
/// <param name="FirstMs">The first open in the window, milliseconds since the epoch, UTC.</param>
/// <param name="LastMs">The last open in the window.</param>
/// <param name="Opens">How many opens in the window.</param>
/// <param name="Days">How many days the code was opened on.</param>
/// <param name="Paths">The paths opened, with a count each, busiest first.</param>
public sealed record LinkOpensRow(string Code, long FirstMs, long LastMs, int Opens, int Days, IReadOnlyDictionary<string, int> Paths);

/// <summary>The card's report: whether opens are kept, the window, and one row a code, newest open first.</summary>
/// <param name="Available">True when the store keeps opens.</param>
/// <param name="Note">Why not, when it does not; null otherwise.</param>
/// <param name="Window">The window read, as its name.</param>
/// <param name="Codes">One row a code.</param>
/// <param name="Keys">The pass-through link keys the configuration names, so the card can say what /api/go offers.</param>
public sealed record LinkOpensReport(bool Available, string? Note, string Window, IReadOnlyList<LinkOpensRow> Codes, IReadOnlyList<string> Keys);

/// <summary>The card's report for a window: the kept days folded to one row a code.</summary>
/// <param name="opens">Where opens are kept.</param>
/// <param name="targets">The pass-through links, for the card's own line.</param>
/// <param name="clock">The clock, so the window is measured from now.</param>
public sealed class ReadLinkOpensHandler(ILinkOpensReader opens, ILinkTargets targets, TimeProvider clock) : IQueryHandler<ReadLinkOpensQuery, LinkOpensReport>
{
    /// <summary>The windows the card offers, by name, and the default.</summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> Windows = new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
    {
        ["7d"] = TimeSpan.FromDays(7),
        ["30d"] = TimeSpan.FromDays(30),
        ["90d"] = TimeSpan.FromDays(90),
    };

    /// <summary>The report; an unknown window is thirty days.</summary>
    /// <param name="query">The window: 7d, 30d or 90d, or none.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<LinkOpensReport> HandleAsync(ReadLinkOpensQuery query, CancellationToken cancellation)
    {
        string? window = query.Window;
        string name = window is not null && Windows.ContainsKey(window) ? window : "30d";
        var availability = await opens.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return new LinkOpensReport(false, availability.Reason, name, [], targets.Keys);
        }
        var days = await opens.DaysAsync(clock.GetUtcNow() - Windows[name], cancellation);
        var rows = days
            .GroupBy(day => day.Code, StringComparer.Ordinal)
            .Select(code => new LinkOpensRow(
                code.Key,
                code.Min(day => day.First).ToUnixTimeMilliseconds(),
                code.Max(day => day.Last).ToUnixTimeMilliseconds(),
                code.Sum(day => day.Opens),
                code.Count(),
                code.Aggregate((IReadOnlyDictionary<string, int>)new Dictionary<string, int>(StringComparer.Ordinal), (kept, day) => LinkOpenFolding.Merge(kept, day.Paths))))
            .OrderByDescending(row => row.LastMs)
            .ToList();
        return new LinkOpensReport(true, null, name, rows, targets.Keys);
    }
}
/// <summary>The card's opens for a window.</summary>
/// <param name="Window">7d, 30d or 90d, or none.</param>
public sealed record ReadLinkOpensQuery(string? Window);
// #endregion read-link-opens

// #region page-open
/// <summary>
/// The open the page reports for itself: the page is served by the rendering service, so the
/// code on its address never reaches this process until the page says so. The page calls this
/// once with the code and the view it opened on, and the pipeline records it like any other
/// tagged request; the handler only says whether the code was one, so the page is answered 204
/// or 400 and never learns anything else.
/// </summary>
public sealed class ReportPageOpenHandler : ICommandHandler<ReportPageOpenCommand, bool>
{
    /// <summary>True when the code is a code and the path is a path the site has.</summary>
    /// <param name="command">The code the page carried and the view it opened on.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<bool> HandleAsync(ReportPageOpenCommand command, CancellationToken cancellation) =>
        Task.FromResult(LinkCodes.IsOne(command.Code) && command.Path is not null && TaggedRequests.PageViews.Contains(command.Path, StringComparer.Ordinal));
}

/// <summary>The open the page reports for itself, recorded by the pipeline when this answers yes, so it is a command.</summary>
/// <param name="Code">The code the page carried.</param>
/// <param name="Path">The view the page opened on, as a path: / or /about.</param>
public sealed record ReportPageOpenCommand(string? Code, string? Path);
// #endregion page-open
