// Where each card's resource is, read from Azure rather than typed: the resource group is listed
// once an hour with the site's identity, and each card finds its resource by type and name. The
// portal links are built from the ids this list returns, so a link can only name a resource Azure
// says is there.
using System.Text.Json;

namespace TheYard.Api;

/// <summary>One resource in the group, as Resource Manager lists it.</summary>
/// <param name="Id">Its full resource id.</param>
/// <param name="Name">Its name; a database's is server/database.</param>
/// <param name="Type">Its type, such as Microsoft.Sql/servers/databases.</param>
public sealed record AzureResource(string Id, string Name, string Type);

/// <summary>The resource group's resources, listed once an hour.</summary>
public sealed class ResourceDirectory(AzureMetrics azure, string resourceGroupId, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<AzureResource> All)? _listed;

    /// <summary>Every resource in the group.</summary>
    public async Task<IReadOnlyList<AzureResource>> AllAsync(CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var now = clock.GetUtcNow();
            if (_listed is { } hit && now - hit.At < TimeSpan.FromHours(1))
            {
                return hit.All;
            }
            using var answer = await azure.GetJsonAsync($"{resourceGroupId}/resources?api-version=2021-04-01", cancellation);
            var all = Parse(answer.RootElement);
            _listed = (now, all);
            return all;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The first resource of a type whose name ends with <paramref name="nameEnd"/>; throws when there is none, which the recorder logs.</summary>
    public async Task<AzureResource> FindAsync(string type, string? nameEnd, CancellationToken cancellation) =>
        Find(await AllAsync(cancellation), type, nameEnd)
        ?? throw new InvalidOperationException($"no {type} named {nameEnd ?? "anything"} in the resource group");

    /// <summary>The first resource of a type, and of a name ending as given when one is; null when none matches.</summary>
    public static AzureResource? Find(IReadOnlyList<AzureResource> all, string type, string? nameEnd) =>
        all.Where(r => string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase))
            .Where(r => nameEnd is null || r.Name.EndsWith(nameEnd, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>A Resource Manager list of resources as records; empty when it holds none.</summary>
    public static IReadOnlyList<AzureResource> Parse(JsonElement answer) =>
        answer.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(r => new AzureResource(AzureMetrics.Text(r, "id") ?? "", AzureMetrics.Text(r, "name") ?? "", AzureMetrics.Text(r, "type") ?? "")).Where(r => r.Id.Length > 0)]
            : [];
}
