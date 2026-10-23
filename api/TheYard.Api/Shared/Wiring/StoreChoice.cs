using TheYard.Application;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// Which store a request gets when it names none and arrived on no site's name, decided from
/// configuration alone, so a slice's module can ask it while registering, before any store is up.
/// </summary>
public static class StoreChoice
{
    /// <summary>
    /// Store:Default where set; else the relational store for the web app serving both sites or a
    /// run with no document store; else the document store, for a run that configured it and said
    /// nothing else.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    public static string DefaultKey(IConfiguration configuration)
    {
        string? configured = configuration["Store:Default"];
        if (configured is not null)
        {
            return configured;
        }
        bool servesBothSites = SiteSettings.From(configuration).StoreSites.Count() > 1;
        return servesBothSites || !DocumentStoreConfigured(configuration) ? StoreKeys.Relational : StoreKeys.Document;
    }

    /// <summary>
    /// True when the deploy named a document store account. A failed substitution leaves a
    /// placeholder, which reads as "no Cosmos DB here", never an address.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    public static bool DocumentStoreConfigured(IConfiguration configuration) =>
        configuration["Cosmos:AccountEndpoint"] is { } endpoint
        && !string.IsNullOrWhiteSpace(endpoint)
        && !endpoint.StartsWith("__", StringComparison.Ordinal);
}
