// The page's report that a visitor reached a stop on the review route, or pressed one of its buttons (ADR: Ten
// minutes, seven stops, the addenda on the reach rows and the second actions), the slice's one command: a report
// carries a stop or an action, a stop outside the route or a name outside the list is refused, a code that is
// not one is dropped rather than refused, and a visitor the site reads as itself is answered and not kept.
using System.Globalization;
using TheYard.Application.Shared;
using TheYard.Application.Shared.Links;

namespace TheYard.Application.Features.Review;

// #region record-review-reach
/// <summary>Hands one reach to the collector, off the request path; true when the report is one, false when it is refused.</summary>
/// <param name="offers">Where reaches are handed: the collector that writes them once a minute.</param>
/// <param name="clock">The server's clock: the day and the time are the server's, never the page's.</param>
public sealed class RecordReviewReachHandler(IReviewReachOffers offers, TimeProvider clock) : ICommandHandler<RecordReviewReachCommand, bool>
{
    /// <summary>
    /// Refuses a report with neither a stop nor an action, a stop that is not on the route and a name that is not
    /// on the list; otherwise keeps it for a person and answers yes. An action alone is kept at stop 0, since the
    /// button was pressed on the opened route.
    /// </summary>
    /// <param name="command">The visitor's hash, the stop or the action, and the code the page carried.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<bool> HandleAsync(RecordReviewReachCommand command, CancellationToken cancellation)
    {
        bool stopIsOne = command.Stop is null || ReviewFolding.IsStop(command.Stop.Value);
        bool actionIsOne = command.Action is null || ReviewActions.IsOne(command.Action);
        if ((command.Stop is null && command.Action is null) || !stopIsOne || !actionIsOne)
        {
            return Task.FromResult(false);
        }
        if (command.Hash is { Length: > 0 } hash)
        {
            var at = clock.GetUtcNow();
            string? code = LinkCodes.IsOne(command.Code) ? command.Code : null;
            offers.Offer(new ReviewReach(hash, at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), command.Stop ?? 0, code, at, command.Action, ReviewActions.ScreenOrNull(command.Screen)));
        }
        return Task.FromResult(true);
    }
}

/// <summary>A visitor reached a stop on the review route, or pressed one of its buttons.</summary>
/// <param name="Hash">The visitor's stable keyed hash, or null for a read the site makes of itself, a scanner or the operator.</param>
/// <param name="Stop">0 for opened, 1 to 7 for the stop reached, or null for a report of an action alone.</param>
/// <param name="Code">The ?r= code the page opened on, or null; one that is not a code is dropped.</param>
/// <param name="Action">The button pressed (<see cref="ReviewActions.Names"/>), or null.</param>
/// <param name="Screen">The first screen the page drew; one the route does not draw is dropped, not refused.</param>
public sealed record RecordReviewReachCommand(string? Hash, int? Stop, string? Code, string? Action = null, string? Screen = null);
// #endregion record-review-reach
