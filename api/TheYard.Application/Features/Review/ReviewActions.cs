// The second actions on the review route (ADR: Ten minutes, seven stops, the addendum on the second actions):
// every button on the first screen and the short path reports one name, and the Admin card asks two questions
// of them. Which names a report may carry, which of them reach the running system or the booking button, and
// the day the actions started counting live here once, so the command, the fold and the funnel read one list.
namespace TheYard.Application.Features.Review;

// #region review-actions
/// <summary>The names a report may carry beside or instead of a stop, and the two figures' rules over them.</summary>
public static class ReviewActions
{
    /// <summary>Every name the page reports: the twelve buttons, links and stops of the first screen and the short path.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "proof", "book", "resume", "recommendations", "stop-quality", "stop-gate", "stop-room", "slip", "tour", "linkedin", "github", "about",
    ];

    /// <summary>The names that reach the running system or the booking button; a resume click alone is not one.</summary>
    public static readonly IReadOnlyList<string> RunningOrBook = ["proof", "stop-quality", "stop-gate", "stop-room", "slip", "book"];

    /// <summary>
    /// The UTC day the actions started counting: the day the version that records them went live. The ship fills
    /// it in when the version is numbered, so it is the landing day and never a guess written ahead of it.
    /// </summary>
    public const string CountedFrom = "2026-11-19";

    /// <summary>Whether a name is one the page may report; null is not one.</summary>
    /// <param name="name">The name reported.</param>
    public static bool IsOne(string? name) => name is not null && Names.Contains(name, StringComparer.Ordinal);

    /// <summary>Whether a visitor reached a second action: any action, or a stop past the opening.</summary>
    /// <param name="row">The visitor's row, folded across the window's days.</param>
    public static bool ReachedASecond(ReviewRow row) => row.Stop > 0 || row.Actions is { Count: > 0 };

    /// <summary>Whether a visitor reached the running system or the booking button: a stop past the opening, or one of those names.</summary>
    /// <param name="row">The visitor's row, folded across the window's days.</param>
    public static bool ReachedRunningOrBook(ReviewRow row) =>
        row.Stop > 0 || (row.Actions ?? []).Any(action => RunningOrBook.Contains(action, StringComparer.Ordinal));

    /// <summary>Two sets of names as one, each name once, in the order <see cref="Names"/> lists them; null reads as none.</summary>
    /// <param name="kept">The names kept.</param>
    /// <param name="added">The names added.</param>
    public static IReadOnlyList<string>? Union(IReadOnlyList<string>? kept, IReadOnlyList<string>? added)
    {
        if (kept is null && added is null)
        {
            return null;
        }
        var all = (kept ?? []).Concat(added ?? []).ToHashSet(StringComparer.Ordinal);
        return [.. Names.Where(all.Contains)];
    }
}
// #endregion review-actions
