// The answer a measured point read gives back: the document and what reading it cost. Its own
// file so the store's operations part stays about the operations; the experiment card is the
// caller that wants the number.
namespace TheYard.Infrastructure.Cosmos;

/// <summary>A point read's answer with its cost, for a caller that wants the number and not only the document.</summary>
/// <param name="Item">The document read, or null when it was not found.</param>
/// <param name="Charge">What the read cost, in request units.</param>
/// <param name="DurationMs">How long the read took, in milliseconds.</param>
public sealed record MeasuredItem<T>(T? Item, double Charge, long DurationMs) where T : class;
