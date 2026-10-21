using TheYard.Application.Features.Links;

namespace TheYard.Api;

// #region configured-link-targets
/// <summary>
/// Where each pass-through link leads, from configuration (Links:Go:{key}): GitHub and LinkedIn
/// cannot count an open on their side, so a tagged link to either goes through this site first.
/// The addresses are configuration, not code, like every other name of this deployment.
/// </summary>
public sealed class ConfiguredLinkTargets : ILinkTargets
{
    private readonly Dictionary<string, Uri> _targets = new(StringComparer.Ordinal);

    /// <summary>The configuration section the addresses live under.</summary>
    public const string Section = "Links:Go";

    /// <summary>The targets the section names; a value that is not an absolute https address is left out.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public ConfiguredLinkTargets(IConfiguration configuration)
    {
        foreach (var child in configuration.GetSection(Section).GetChildren())
        {
            if (Uri.TryCreate(child.Value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                _targets[child.Key.ToLowerInvariant()] = uri;
            }
        }
    }

    public Uri? For(string key) => _targets.GetValueOrDefault(key);

    public IReadOnlyList<string> Keys => [.. _targets.Keys.Order(StringComparer.Ordinal)];
}
// #endregion configured-link-targets
