namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The only address finder (Steve, 10/24 12:58: "I'm not sure about the 3rd party for email"): it asks no one,
/// spends nothing and answers nothing, so an address comes only from a public source the lead run recorded
/// with its URL. No account, no key, no setting.
/// </summary>
public sealed class NoFinder : IAddressFinder
{
    /// <summary>What the card and the pass log name as the finder.</summary>
    public const string NotConfigured = "none (public sources only)";

    /// <inheritdoc />
    public string Name => NotConfigured;

    /// <inheritdoc />
    public Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct) => Task.FromResult<FoundAddress?>(null);
}
