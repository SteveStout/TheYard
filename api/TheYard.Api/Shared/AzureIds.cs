namespace TheYard.Api;

/// <summary>
/// The managed identity's client id and the subscription the site's resources live in: the
/// deploy's settings when it gives them, otherwise the identity the web app runs as and its
/// subscription. Identifiers, not secrets. One place, so every step that signs in to Azure asks
/// as the same identity.
/// </summary>
public static class AzureIds
{
    /// <summary>The managed identity's client id.</summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string ClientId(IConfiguration configuration) =>
        configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5";

    /// <summary>The subscription the site's resources live in.</summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string SubscriptionId(IConfiguration configuration) =>
        configuration["Azure:SubscriptionId"] ?? "df3b718c-6d99-4904-8102-6f865941f640";
}
