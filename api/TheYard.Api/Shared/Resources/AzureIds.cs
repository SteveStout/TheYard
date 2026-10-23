namespace TheYard.Api.Shared.Resources;

/// <summary>
/// The managed identity's client id and the subscription the site's resources live in: the
/// deploy's settings when it gives them, otherwise appsettings.json, which names the identity the
/// web app runs as and its subscription. Identifiers, not secrets. One place, so every step that signs in to Azure asks
/// as the same identity.
/// </summary>
public static class AzureIds
{
    /// <summary>The managed identity's client id.</summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string ClientId(IConfiguration configuration) =>
        configuration["Azure:ClientId"] ?? throw new InvalidOperationException("Azure:ClientId is not configured.");

    /// <summary>The subscription the site's resources live in.</summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string SubscriptionId(IConfiguration configuration) =>
        configuration["Azure:SubscriptionId"] ?? throw new InvalidOperationException("Azure:SubscriptionId is not configured.");
}
