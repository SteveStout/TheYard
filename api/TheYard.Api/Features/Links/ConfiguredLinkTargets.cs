using TheYard.Application.Features.Links;

namespace TheYard.Api.Features.Links;

// #region configured-link-targets
/// <summary>
/// Where each pass-through link leads, from configuration (Links:Go:{key}): GitHub and LinkedIn
/// cannot count an open on their side, so a tagged link to either goes through this site first.
/// The addresses are configuration, not code, like every other name of this deployment. The one
/// key "email" leads to a mailto address (Links:Email), so the Email Steven links on /about and the
/// review route's finish panel are counted too and no drawn page carries the address (ADR-123's addendum).
/// </summary>
public sealed class ConfiguredLinkTargets : ILinkTargets
{
    private readonly Dictionary<string, Uri> _targets = new(StringComparer.Ordinal);

    /// <summary>The configuration section the addresses live under.</summary>
    public const string Section = "Links:Go";

    /// <summary>The one mailto address, kept apart from the https section so no other key can hold one.</summary>
    public const string Email = "Links:Email";

    /// <summary>The targets the section names, a value that is not an absolute https address left out, and the email key.</summary>
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

        if (Uri.TryCreate(configuration[Email], UriKind.Absolute, out var email) && email.Scheme == Uri.UriSchemeMailto)
        {
            _targets["email"] = email;
        }
    }

    public Uri? For(string key) => _targets.GetValueOrDefault(key);

    public IReadOnlyList<string> Keys => [.. _targets.Keys.Order(StringComparer.Ordinal)];
}
// #endregion configured-link-targets
