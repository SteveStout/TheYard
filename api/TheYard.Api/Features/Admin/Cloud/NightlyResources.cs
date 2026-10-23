namespace TheYard.Api.Features.Admin.Cloud;

// #region nightly-resources
/// <summary>
/// The names behind the Admin tab's nightly card, read from configuration (Nightly in
/// appsettings.json, which a web app setting overrides), so no name of that deployment is written
/// in the code: the Function app, its plan and storage account, the resource group they live in,
/// and the repository and workflow file the Function dispatches (ADR: The Nightly Function on the
/// Admin tab).
/// </summary>
/// <param name="Group">The resource group the Function lives in.</param>
/// <param name="Function">The Function app, which the card finds by type and this name.</param>
/// <param name="Plan">The Function's plan, a line of the bill.</param>
/// <param name="Storage">The Function's storage account, a line of the bill.</param>
/// <param name="Repo">The repository whose workflow runs the card lists, owner/name.</param>
/// <param name="Workflow">The workflow file the Function dispatches.</param>
public sealed record NightlyResources(string Group, string Function, string Plan, string Storage, string Repo, string Workflow)
{
    /// <summary>The names as configuration gives them; a missing one stops the host with its key named.</summary>
    /// <param name="configuration">The host's configuration.</param>
    public static NightlyResources From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Nightly");
        string Named(string key) => section[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Nightly:{key} is not configured.");
        return new(Named("Group"), Named("Function"), Named("Plan"), Named("Storage"), Named("Repo"), Named("Workflow"));
    }

    /// <summary>The resource group's path in a subscription.</summary>
    /// <param name="subscription">The subscription's id.</param>
    public string GroupPath(string subscription) => $"/subscriptions/{subscription}/resourceGroups/{Group}";

    /// <summary>The Function app's path in a subscription, which the card's portal link is built from.</summary>
    /// <param name="subscription">The subscription's id.</param>
    public string FunctionPath(string subscription) => $"{GroupPath(subscription)}/providers/Microsoft.Web/sites/{Function}";

    /// <summary>The names the bill is matched on for the card's three cost lines: the Function, its plan and its storage.</summary>
    public IReadOnlyList<string> CostNames => [Function, Plan, Storage];
}
// #endregion nightly-resources
