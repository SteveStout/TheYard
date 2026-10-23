namespace TheYard.Api.Shared.Resources;

// #region azure-resources
/// <summary>
/// The names of the resources this deployment runs on, read from configuration
/// (Azure:Resources in appsettings.json, which a web app setting overrides), so no name of this
/// deployment is written in the code: the cards find each resource by its type and this name, the
/// bill is matched on it, and LiteralScanTests fails the build on one written in (ADR: The onion
/// cut into vertical slices, the facts the Admin tab reads).
/// </summary>
/// <param name="Group">The resource group.</param>
/// <param name="Site">The web app this process runs as.</param>
/// <param name="SqlDatabase">The database the SQL card reads.</param>
/// <param name="Cosmos">The Cosmos DB account.</param>
/// <param name="Registry">The container registry.</param>
/// <param name="Insights">The Log Analytics workspace the insights card's cost is billed under.</param>
/// <param name="Mail">The Communication Services resource.</param>
/// <param name="Edge">The Netlify project in front of the sites.</param>
/// <param name="PublicSite">The public address the edge serves.</param>
/// <param name="EdgeRetires">When the edge is meant to retire (Edge:Retires).</param>
public sealed record AzureResources(string Group, string Site, string SqlDatabase, string Cosmos, string Registry, string Insights, string Mail, string Edge, Uri PublicSite, DateOnly EdgeRetires)
{
    /// <summary>
    /// The names as configuration gives them; a missing one stops the host with its key named. The
    /// resource group is App Service's own WEBSITE_RESOURCE_GROUP where the platform sets it.
    /// </summary>
    /// <param name="configuration">The host's configuration.</param>
    public static AzureResources From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Azure:Resources");
        string Named(string key) => section[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Azure:Resources:{key} is not configured.");
        string retires = configuration["Edge:Retires"] ?? throw new InvalidOperationException("Edge:Retires is not configured.");
        return new(configuration["WEBSITE_RESOURCE_GROUP"] is { Length: > 0 } group ? group : Named("Group"), Named("Site"), Named("SqlDatabase"), Named("Cosmos"), Named("Registry"), Named("Insights"), Named("Mail"), Named("Edge"), new Uri(Named("PublicSite")), DateOnly.Parse(retires, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The resource group's path in a subscription.</summary>
    /// <param name="subscription">The subscription's id.</param>
    public string GroupPath(string subscription) => $"/subscriptions/{subscription}/resourceGroups/{Group}";

    /// <summary>This web app's path in a subscription.</summary>
    /// <param name="subscription">The subscription's id.</param>
    public string SitePath(string subscription) => $"{GroupPath(subscription)}/providers/Microsoft.Web/sites/{Site}";
}
// #endregion azure-resources
