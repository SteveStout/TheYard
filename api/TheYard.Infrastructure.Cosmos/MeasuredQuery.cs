// The answer a measured query gives back: every item, with the charge of every page added up,
// the page count and the time. Its own file so the store's operations part stays about the
// operations; the experiment card is the caller that wants the numbers.
namespace TheYard.Infrastructure.Cosmos;

/// <summary>A query's answer with its cost: every page's charge added up, the page count, and the wall clock.</summary>
/// <param name="Items">Every item the query returned, across all pages.</param>
/// <param name="Charge">What all pages cost together, in request units.</param>
/// <param name="Pages">How many pages the query took.</param>
/// <param name="DurationMs">How long the query took end to end, in milliseconds.</param>
public sealed record MeasuredQuery<T>(IReadOnlyList<T> Items, double Charge, int Pages, long DurationMs);
