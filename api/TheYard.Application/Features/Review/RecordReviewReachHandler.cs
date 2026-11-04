// The page's report that a visitor reached a stop on the review route (ADR: Ten minutes, seven stops, the
// addendum on the reach rows), the slice's one command: a stop outside the route is refused, a code that is
// not one is dropped rather than refused, and a visitor the site reads as itself is answered and not kept.
using System.Globalization;
using TheYard.Application.Shared;
using TheYard.Application.Shared.Links;

namespace TheYard.Application.Features.Review;

// #region record-review-reach
/// <summary>Hands one reach to the collector, off the request path; true when the stop is one, false when it is refused.</summary>
/// <param name="offers">Where reaches are handed: the collector that writes them once a minute.</param>
/// <param name="clock">The server's clock: the day and the time are the server's, never the page's.</param>
public sealed class RecordReviewReachHandler(IReviewReachOffers offers, TimeProvider clock) : ICommandHandler<RecordReviewReachCommand, bool>
{
    /// <summary>Refuses a stop that is not on the route; otherwise keeps it for a person and answers yes.</summary>
    /// <param name="command">The visitor's hash, the stop and the code the page carried.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<bool> HandleAsync(RecordReviewReachCommand command, CancellationToken cancellation)
    {
        if (!ReviewFolding.IsStop(command.Stop))
        {
            return Task.FromResult(false);
        }
        if (command.Hash is { Length: > 0 } hash)
        {
            var at = clock.GetUtcNow();
            string? code = LinkCodes.IsOne(command.Code) ? command.Code : null;
            offers.Offer(new ReviewReach(hash, at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), command.Stop, code, at));
        }
        return Task.FromResult(true);
    }
}

/// <summary>A visitor reached a stop on the review route.</summary>
/// <param name="Hash">The visitor's stable keyed hash, or null for a read the site makes of itself, a scanner or the operator.</param>
/// <param name="Stop">0 for opened, 1 to 7 for the stop reached.</param>
/// <param name="Code">The ?r= code the page opened on, or null; one that is not a code is dropped.</param>
public sealed record RecordReviewReachCommand(string? Hash, int Stop, string? Code);
// #endregion record-review-reach
