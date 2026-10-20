using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

// #region vehicle-endpoint-status-tests
/// <summary>Every status code the Vehicles slice's routes declare, one case each through the real host (ADR: The onion cut into vertical slices, rule 7).</summary>
public sealed class VehicleEndpointStatusTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/vehicles", 200)]
    [Covers("GET", "/api/vehicles", 400)]
    [Covers("GET", "/api/facets", 200)]
    [Covers("GET", "/api/vehicles/{id}", 200)]
    [Covers("GET", "/api/vehicles/{id}", 404)]
    public async Task The_catalogue_answers_200_a_bad_query_400_and_an_unknown_vehicle_404()
    {
        var client = factory.CreateClient();

        var page = await client.GetAsync("/api/vehicles?limit=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var body = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        string id = body.RootElement.GetProperty("vehicles")[0].GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/vehicles?status=sideways")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/facets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/vehicles/no-vehicle-has-this-id")).StatusCode);
    }
}
// #endregion vehicle-endpoint-status-tests
