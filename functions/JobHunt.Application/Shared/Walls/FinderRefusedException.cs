namespace JobHunt.Application.Shared.Walls;

/// <summary>
/// An address finder refused to answer: its account is restricted or its key is not accepted, so every lookup until
/// a person clears it would come back the same. Not "no address for this person", which is what a null answer means.
/// </summary>
/// <param name="finder">The finder's name.</param>
/// <param name="reason">What it answered, such as "HTTP 429 restricted_account".</param>
/// <param name="called">False when the wall stopped the lookup before any call went out, so nothing was spent.</param>
public sealed class FinderRefusedException(string finder, string reason, bool called)
    : Exception($"{finder} refused: {reason}")
{
    /// <summary>The finder's name.</summary>
    public string Finder { get; } = finder;

    /// <summary>What it answered.</summary>
    public string Reason { get; } = reason;

    /// <summary>False when no call went out.</summary>
    public bool Called { get; } = called;
}
