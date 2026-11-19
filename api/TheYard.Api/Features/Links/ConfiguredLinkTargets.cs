using TheYard.Application.Features.Links;

namespace TheYard.Api.Features.Links;

// #region configured-link-targets
/// <summary>
/// Where each pass-through link leads, from configuration (Links:Go:{key}): GitHub and LinkedIn
/// cannot count an open on their side, so a tagged link to either goes through this site first.
/// The addresses are configuration, not code, like every other name of this deployment. Two keys lead
/// to a mailto address: "email" (Links:Email), so the email contact links on /about and the review
/// route's finish panel and the "Where next" footer's contact line are counted too and no drawn page
/// carries the address (ADR-123's and ADR-108's addenda); and "book" (Links:Book), the review route's
/// "Book 20 minutes", the same address with the subject and the body of a request for a time, counted
/// apart from "email" (ADR-123's addendum on the second actions).
/// </summary>
public sealed class ConfiguredLinkTargets : ILinkTargets
{
    private readonly Dictionary<string, Uri> _targets = new(StringComparer.Ordinal);

    /// <summary>The configuration section the addresses live under.</summary>
    public const string Section = "Links:Go";

    /// <summary>The contact mailto address, kept apart from the https section so no other key can hold one.</summary>
    public const string Email = "Links:Email";

    /// <summary>The booking mailto address, kept apart from the https section like <see cref="Email"/>.</summary>
    public const string Book = "Links:Book";

    /// <summary>The targets the section names, a value that is not an absolute https address left out, and the two mailto keys.</summary>
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

        foreach (var (key, setting) in new[] { ("email", Email), ("book", Book) })
        {
            if (Uri.TryCreate(configuration[setting], UriKind.Absolute, out var mailto) && mailto.Scheme == Uri.UriSchemeMailto)
            {
                _targets[key] = mailto;
            }
        }
    }

    public Uri? For(string key) => _targets.GetValueOrDefault(key);

    public IReadOnlyList<string> Keys => [.. _targets.Keys.Order(StringComparer.Ordinal)];
}
// #endregion configured-link-targets
