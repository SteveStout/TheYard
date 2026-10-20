// What the platform tells this process about where it runs, read once. App Service names the web
// app in WEBSITE_SITE_NAME and its managed identity's door in IDENTITY_ENDPOINT and IDENTITY_HEADER;
// Program.cs reads all three from configuration into this record and every reader is handed it by
// constructor, so no behaviour reads the environment itself and a test sets it like any other value
// (ADR: The onion cut into vertical slices, rule 6).
namespace TheYard.Api;

// #region host-identity
/// <summary>
/// Where this process runs, as the platform said at startup: the web app's name and the managed
/// identity's door. All three are null off App Service, which is a developer's machine and every
/// test host that does not set them.
/// </summary>
/// <param name="SiteName">App Service's WEBSITE_SITE_NAME, or null.</param>
/// <param name="IdentityEndpoint">App Service's IDENTITY_ENDPOINT, or null.</param>
/// <param name="IdentityHeader">App Service's IDENTITY_HEADER, the sandbox's own secret, or null.</param>
public sealed record HostIdentity(string? SiteName, string? IdentityEndpoint, string? IdentityHeader)
{
    /// <summary>Nowhere in particular: no web app name and no identity door.</summary>
    public static HostIdentity None { get; } = new(null, null, null);

    /// <summary>True when the platform has named an identity endpoint, which is how this process knows it is on App Service.</summary>
    public bool OnAppService => !string.IsNullOrWhiteSpace(IdentityEndpoint) && !string.IsNullOrWhiteSpace(IdentityHeader);

    /// <summary>The three settings from configuration, which carries the environment; an empty value is none.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static HostIdentity From(IConfiguration configuration) =>
        new(ValueOrNull(configuration["WEBSITE_SITE_NAME"]), ValueOrNull(configuration["IDENTITY_ENDPOINT"]), ValueOrNull(configuration["IDENTITY_HEADER"]));

    /// <summary>The name and whether there is a door; never the header, which proves the caller is inside the sandbox.</summary>
    public override string ToString() => $"HostIdentity {{ SiteName = {SiteName}, OnAppService = {OnAppService} }}";

    private static string? ValueOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
// #endregion host-identity
