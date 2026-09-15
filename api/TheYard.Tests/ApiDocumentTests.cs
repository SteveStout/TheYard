using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The API's description of itself (ADR: The API describes itself): the
/// document is served, the admin surface is not in it, every public endpoint
/// is, and every operation says what it is called, what it does, what it
/// answers, and whether it needs a session.
///
/// <para>Every set this class compares against is read from the running host
/// rather than typed here: the routing table's own description of the
/// endpoints is the list a new endpoint joins by being mapped, so a route
/// added next month is checked without anybody remembering this file.</para>
/// </summary>
public class ApiDocumentTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region document
    /// <summary>One mapped endpoint, as the host describes it: the method, the path, and whether it needs a session.</summary>
    private sealed record Mapped(string Method, string Path, bool Protected);

    /// <summary>
    /// Every endpoint the host maps, admin ones included, read from the same
    /// description the generator reads. The path is the route template with a
    /// leading slash, which is how the document keys it.
    /// </summary>
    private IReadOnlyList<Mapped> Endpoints() =>
        factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Select(description => new Mapped(
                description.HttpMethod ?? "",
                "/" + (description.RelativePath ?? "").TrimStart('/'),
                description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any()
                    && !description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()))
            .ToList();

    private static bool IsAdmin(string path) =>
        path.StartsWith("/" + ApiDocument.AdminPrefix, StringComparison.OrdinalIgnoreCase);

    private async Task<JsonDocument> DocumentAsync()
    {
        var response = await _client.GetAsync(ApiDocument.DocumentRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Every operation in the document as (method, path, operation), the method upper-cased the way the host reports it.</summary>
    private static List<(string Method, string Path, JsonElement Operation)> Operations(JsonDocument document)
    {
        var operations = new List<(string, string, JsonElement)>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                operations.Add((operation.Name.ToUpperInvariant(), path.Name, operation.Value));
            }
        }
        return operations;
    }

    [Fact]
    public async Task The_document_is_served_and_says_what_it_describes()
    {
        using var document = await DocumentAsync();

        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal(ApiDocument.Title, document.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("info").GetProperty("description").GetString()));
    }

    /// <summary>
    /// The rule that silently breaks a year later: an operator endpoint under
    /// a new name, or the filter loosened by a refactor, and the public
    /// document becomes an index of the parts of the site a stranger has the
    /// least business reading. The second assertion is what makes the first
    /// one worth having: a filter that passes because there is nothing to
    /// filter has not been tested.
    /// </summary>
    [Fact]
    public async Task No_admin_route_is_in_the_public_document()
    {
        using var document = await DocumentAsync();

        var leaked = Operations(document)
            .Where(operation => IsAdmin(operation.Path))
            .Select(operation => $"{operation.Method} {operation.Path}")
            .ToList();
        Assert.True(leaked.Count == 0, "these operator endpoints are in the public document: " + string.Join(", ", leaked));

        int admin = Endpoints().Count(endpoint => IsAdmin(endpoint.Path));
        Assert.True(admin >= 10, $"only {admin} admin endpoints are mapped, so this test is not exercising the filter");
    }

    [Fact]
    public async Task Every_public_endpoint_is_in_the_document_and_nothing_else_is()
    {
        using var document = await DocumentAsync();

        var mapped = Endpoints()
            .Where(endpoint => !IsAdmin(endpoint.Path))
            .Select(endpoint => $"{endpoint.Method} {endpoint.Path}")
            .ToHashSet(StringComparer.Ordinal);
        var documented = Operations(document)
            .Select(operation => $"{operation.Method} {operation.Path}")
            .ToHashSet(StringComparer.Ordinal);

        // A floor, so a document that describes three endpoints of forty-one
        // cannot pass by matching a routing table that was read wrongly.
        Assert.True(mapped.Count >= 20, $"only {mapped.Count} public endpoints were read from the host");
        Assert.True(
            mapped.SetEquals(documented),
            "mapped but not documented: [" + string.Join(", ", mapped.Except(documented))
            + "]; documented but not mapped: [" + string.Join(", ", documented.Except(mapped)) + "]");
    }

    /// <summary>
    /// The gate that keeps the document from rotting the way the README did:
    /// a public endpoint cannot ship without a name, a sentence, and its
    /// responses. A 2xx other than 204 declares what it carries; every
    /// response is a numbered status rather than a default.
    /// </summary>
    [Fact]
    public async Task Every_operation_carries_an_operation_id_a_summary_and_its_responses()
    {
        using var document = await DocumentAsync();
        var wrong = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach ((string method, string path, JsonElement operation) in Operations(document))
        {
            string where = $"{method} {path}";
            string? id = operation.TryGetProperty("operationId", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                wrong.Add($"{where} has no operation id");
            }
            else if (!ids.Add(id))
            {
                wrong.Add($"{where} repeats the operation id {id}");
            }

            if (!operation.TryGetProperty("summary", out var summary) || string.IsNullOrWhiteSpace(summary.GetString()))
            {
                wrong.Add($"{where} has no summary");
            }

            if (!operation.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
            {
                wrong.Add($"{where} declares no responses");
                continue;
            }

            bool success = false;
            foreach (var response in responses.EnumerateObject())
            {
                if (!Regex.IsMatch(response.Name, @"^\d{3}$"))
                {
                    wrong.Add($"{where} declares a response called '{response.Name}' rather than a status code");
                    continue;
                }

                if (response.Name[0] != '2')
                {
                    continue;
                }

                success = true;
                if (response.Name != "204"
                    && (!response.Value.TryGetProperty("content", out var content) || !content.EnumerateObject().Any()))
                {
                    wrong.Add($"{where} declares a {response.Name} with no content type or shape");
                }
            }

            if (!success)
            {
                wrong.Add($"{where} declares no success response");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// The failures, because a document that only shows 200s is the tell of a
    /// generated document nobody read. The three are mechanical: a session is
    /// checked before a protected handler runs, a route parameter names
    /// something that can be missing, and a body can fail to parse or be
    /// refused.
    /// </summary>
    [Fact]
    public async Task Every_operation_declares_the_failures_it_can_answer()
    {
        using var document = await DocumentAsync();
        var guarded = Endpoints()
            .Where(endpoint => endpoint.Protected && !IsAdmin(endpoint.Path))
            .Select(endpoint => $"{endpoint.Method} {endpoint.Path}")
            .ToHashSet(StringComparer.Ordinal);
        var wrong = new List<string>();

        foreach ((string method, string path, JsonElement operation) in Operations(document))
        {
            string where = $"{method} {path}";
            var declared = operation.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToHashSet(StringComparer.Ordinal);

            if (guarded.Contains(where) && !declared.Contains("401"))
            {
                wrong.Add($"{where} needs a session and does not declare its 401");
            }

            if (path.Contains('{') && !declared.Contains("404"))
            {
                wrong.Add($"{where} takes a route parameter and does not declare its 404");
            }

            if (operation.TryGetProperty("requestBody", out _) && !declared.Contains("400"))
            {
                wrong.Add($"{where} reads a body and does not declare its 400");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// The session is a bearer token the browser carries in a cookie and a
    /// client can carry in a header, so the document declares both, and marks
    /// exactly the operations that will answer 401 without one. Exactly: a
    /// lock on a public read would send a client looking for a token it does
    /// not need, and a missing lock on a write is the document lying.
    /// </summary>
    [Fact]
    public async Task The_schemes_are_declared_and_only_the_protected_operations_require_one()
    {
        using var document = await DocumentAsync();

        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        var bearer = schemes.GetProperty(ApiDocument.BearerScheme);
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var cookie = schemes.GetProperty(ApiDocument.CookieScheme);
        Assert.Equal("apiKey", cookie.GetProperty("type").GetString());
        Assert.Equal("cookie", cookie.GetProperty("in").GetString());
        Assert.Equal(TokenIssuer.CookieName, cookie.GetProperty("name").GetString());

        // The operator endpoints are protected too, and are not in the
        // document by rule 1, so they are not this rule's business.
        var guarded = Endpoints()
            .Where(endpoint => endpoint.Protected && !IsAdmin(endpoint.Path))
            .Select(endpoint => $"{endpoint.Method} {endpoint.Path}")
            .ToHashSet(StringComparer.Ordinal);
        var locked = Operations(document)
            .Where(operation => operation.Operation.TryGetProperty("security", out var security)
                && security.EnumerateArray().Any(requirement => requirement.TryGetProperty(ApiDocument.BearerScheme, out _)))
            .Select(operation => $"{operation.Method} {operation.Path}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(guarded.Count >= 5, $"only {guarded.Count} protected endpoints were read from the host");
        Assert.True(
            guarded.SetEquals(locked),
            "protected but not locked in the document: [" + string.Join(", ", guarded.Except(locked))
            + "]; locked but not protected: [" + string.Join(", ", locked.Except(guarded)) + "]");
    }

    /// <summary>
    /// Every amount here is whole dollars and every instant is milliseconds,
    /// and a document that offered "integer or string" for each of them would
    /// be describing a JSON option rather than the API. Read from every
    /// schema in the document, the nested ones included.
    /// </summary>
    [Fact]
    public async Task Every_number_in_the_document_is_a_number_and_nothing_else()
    {
        using var document = await DocumentAsync();
        var wrong = new List<string>();

        void Walk(JsonElement element, string where)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array)
                {
                    var names = type.EnumerateArray().Select(name => name.GetString()).ToList();
                    if (names.Contains("string") && (names.Contains("integer") || names.Contains("number")))
                    {
                        wrong.Add($"{where} is typed [{string.Join(", ", names)}]");
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, where + "/" + property.Name);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, where + "/" + index++);
                }
            }
        }

        Walk(document.RootElement.GetProperty("components").GetProperty("schemas"), "schemas");
        Walk(document.RootElement.GetProperty("paths"), "paths");

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// Behind the edge the container sees the origin's host, and a document
    /// that named it would hand every reader an address they should not use.
    /// With the site's address configured, that is the server; without one,
    /// the document names none and a client uses the host it read it from.
    /// </summary>
    [Fact]
    public async Task The_document_names_the_site_as_its_server_only_when_the_site_has_an_address()
    {
        using var unnamed = await DocumentAsync();
        Assert.False(
            unnamed.RootElement.TryGetProperty("servers", out var none) && none.GetArrayLength() > 0,
            "with no Site:Url the document should name no server");

        using var named = factory.WithWebHostBuilder(host => host.UseSetting("Site:Url", "https://theyard.example/"));
        using var response = await named.CreateClient().GetAsync(ApiDocument.DocumentRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var servers = document.RootElement.GetProperty("servers");
        Assert.Equal(1, servers.GetArrayLength());
        Assert.Equal("https://theyard.example", servers[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task The_reference_page_is_served_from_the_same_container()
    {
        var response = await _client.GetAsync(ApiDocument.ReferenceRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(ApiDocument.Title, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
    // #endregion document
}
