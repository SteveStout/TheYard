using TheYard.Application;

namespace TheYard.Api;

/// <summary>
/// The sites this process answers to, read from configuration: a list, Sites:0:Host and
/// Sites:0:Store and so on, where a site with no store is a guest application (ADR: The onion cut
/// into vertical slices, rule 3). A web app configured before the list existed carries one setting
/// per site instead, Hosts:Sql, Hosts:Cosmos and Hosts:Shed; those are read as the same three sites,
/// so a process rolled onto an older web app's settings keeps every name it had.
/// </summary>
public static class SiteSettings
{
    /// <summary>The configured sites, from the list when there is one, else from the settings named per site, with Site:Url for links.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static SiteHosts From(IConfiguration configuration)
    {
        var listed = configuration.GetSection("Sites").GetChildren()
            .Select(site => new Site(site["Host"] ?? "", site["Store"]))
            .ToList();
        if (listed.Count > 0)
        {
            return new SiteHosts(listed, configuration["Site:Url"]);
        }
        return new SiteHosts(
        [
            new Site(configuration["Hosts:Sql"] ?? "", StoreKeys.Relational),
            new Site(configuration["Hosts:Cosmos"] ?? "", StoreKeys.Document),
            new Site(configuration["Hosts:Shed"] ?? "", null),
        ], configuration["Site:Url"]);
    }
}
