using System.Text.Json;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The site asking Azure about itself, on either host (ADR: One plan, two
/// sites). Container Instances and App Service hand out identity tokens from
/// different doors and describe the thing that runs the image in different
/// shapes. Neither host exists on the machine that runs this suite, so the
/// request and the two shapes are pure functions and these tests hand them
/// what each platform actually sends.
/// </summary>
public class AzureSelfTests
{
    private const string ClientId = "00000000-0000-0000-0000-000000000000";

    // #region identity-doors
    [Fact]
    public void With_no_endpoint_named_the_token_request_goes_to_the_metadata_address()
    {
        using var request = IdentityTokens.RequestFor("https://management.azure.com/", ClientId, null, null);

        Assert.StartsWith(IdentityTokens.MetadataAddress + "?api-version=2018-02-01&", request.RequestUri!.ToString());
        Assert.Contains("resource=https%3A%2F%2Fmanagement.azure.com%2F", request.RequestUri.ToString());
        Assert.Contains("client_id=" + ClientId, request.RequestUri.ToString());
        Assert.Equal("true", Assert.Single(request.Headers.GetValues("Metadata")));
        Assert.False(request.Headers.Contains("X-IDENTITY-HEADER"));
    }

    [Fact]
    public void On_app_service_the_token_request_goes_to_the_endpoint_the_platform_named_with_its_header()
    {
        using var request = IdentityTokens.RequestFor(
            "https://api.applicationinsights.io", ClientId, "http://127.0.0.1:41741/msi/token", "a-sandbox-secret");

        Assert.StartsWith("http://127.0.0.1:41741/msi/token?api-version=2019-08-01&", request.RequestUri!.ToString());
        Assert.Contains("resource=https%3A%2F%2Fapi.applicationinsights.io", request.RequestUri.ToString());
        Assert.Contains("client_id=" + ClientId, request.RequestUri.ToString());
        Assert.Equal("a-sandbox-secret", Assert.Single(request.Headers.GetValues("X-IDENTITY-HEADER")));
        Assert.False(request.Headers.Contains("Metadata"));
    }

    [Theory]
    [InlineData("http://127.0.0.1:41741/msi/token", null)]
    [InlineData(null, "a-sandbox-secret")]
    [InlineData("", "")]
    public void Half_a_door_is_no_door(string? endpoint, string? header)
    {
        // Both variables or neither: an endpoint with no header would be
        // refused, and a header with no endpoint has nowhere to go.
        using var request = IdentityTokens.RequestFor("https://management.azure.com/", ClientId, endpoint, header);

        Assert.StartsWith(IdentityTokens.MetadataAddress, request.RequestUri!.ToString());
    }
    // #endregion identity-doors

    [Theory]
    [InlineData("/subscriptions/s/resourceGroups/RG/providers/Microsoft.Web/sites/APP-THEYARD-SS-X", true)]
    [InlineData("/subscriptions/s/resourcegroups/rg/providers/microsoft.web/sites/app", true)]
    [InlineData("/subscriptions/s/resourceGroups/RG/providers/Microsoft.ContainerInstance/containerGroups/aci-theyard-ss", false)]
    public void The_resource_id_says_which_kind_of_host_this_is(string resourceId, bool site) =>
        Assert.Equal(site, AzureSelf.IsSite(resourceId));

    [Fact]
    public void A_container_group_reports_its_state_its_restarts_and_its_last_three_events_newest_first()
    {
        using var group = JsonDocument.Parse("""
            {
              "properties": {
                "instanceView": { "state": "Running" },
                "containers": [ {
                  "properties": {
                    "image": "registry.example/theyard:v154",
                    "instanceView": {
                      "restartCount": 2,
                      "currentState": { "state": "Running" },
                      "events": [
                        { "name": "Pulling", "count": 1, "lastTimestamp": "2026-09-19T20:31:00Z", "message": "pulling" },
                        { "name": "Pulled", "count": 1, "lastTimestamp": "2026-09-19T20:32:00Z", "message": "pulled" },
                        { "name": "Started", "count": 3, "lastTimestamp": "2026-09-19T20:33:00Z", "message": "started" },
                        { "name": "Killing", "count": 1, "lastTimestamp": "2026-09-19T20:30:00Z", "message": "killing" }
                      ]
                    }
                  }
                } ]
              }
            }
            """);

        var card = JsonSerializer.SerializeToElement(AzureSelf.ShapeGroup(group.RootElement, DateTimeOffset.UnixEpoch));

        Assert.True(card.GetProperty("available").GetBoolean());
        Assert.Equal("container-instances", card.GetProperty("host").GetString());
        Assert.Equal("Running", card.GetProperty("group_state").GetString());
        Assert.Equal("Running", card.GetProperty("container_state").GetString());
        Assert.Equal(2, card.GetProperty("restart_count").GetInt32());
        Assert.Equal("registry.example/theyard:v154", card.GetProperty("image").GetString());
        string[] newestFirst = ["Started", "Pulled", "Pulling"];
        Assert.Equal(newestFirst, card.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray());
    }

    // #region site-shape
    [Fact]
    public void A_web_app_reports_its_state_its_image_and_the_plan_it_shares_and_claims_no_restart_count()
    {
        using var site = JsonDocument.Parse("""
            {
              "location": "West US 3",
              "properties": {
                "state": "Running",
                "availabilityState": "Normal",
                "serverFarmId": "/subscriptions/s/resourceGroups/RG/providers/Microsoft.Web/serverfarms/PLAN-THEYARD-SS"
              }
            }
            """);
        using var config = JsonDocument.Parse("""
            { "properties": { "linuxFxVersion": "DOCKER|registry.example/theyard:v155", "alwaysOn": true, "healthCheckPath": "/healthz" } }
            """);
        using var plan = JsonDocument.Parse("""
            { "name": "PLAN-THEYARD-SS", "sku": { "name": "B1", "tier": "Basic" }, "properties": { "numberOfSites": 2 } }
            """);

        var card = JsonSerializer.SerializeToElement(
            AzureSelf.ShapeSite(site.RootElement, config.RootElement, plan.RootElement, DateTimeOffset.UnixEpoch));

        Assert.True(card.GetProperty("available").GetBoolean());
        Assert.Equal("app-service", card.GetProperty("host").GetString());
        Assert.Equal("Running", card.GetProperty("group_state").GetString());
        Assert.Equal("Normal", card.GetProperty("availability").GetString());
        Assert.Equal("registry.example/theyard:v155", card.GetProperty("image").GetString());
        Assert.True(card.GetProperty("always_on").GetBoolean());
        Assert.Equal("/healthz", card.GetProperty("health_check_path").GetString());
        Assert.Equal("PLAN-THEYARD-SS", card.GetProperty("plan_name").GetString());
        Assert.Equal("B1", card.GetProperty("plan_sku").GetString());
        Assert.Equal(2, card.GetProperty("plan_sites").GetInt32());
        Assert.Equal("West US 3", card.GetProperty("region").GetString());
        // App Service keeps neither where this identity can read them, and a
        // zero here would be a number nobody measured.
        Assert.False(card.TryGetProperty("restart_count", out _));
        Assert.False(card.TryGetProperty("container_state", out _));
        Assert.Empty(card.GetProperty("events").EnumerateArray());
    }

    [Fact]
    public void A_web_app_whose_configuration_and_plan_could_not_be_read_still_has_a_card()
    {
        using var site = JsonDocument.Parse("""{ "properties": { "state": "Stopped" } }""");

        var card = JsonSerializer.SerializeToElement(AzureSelf.ShapeSite(site.RootElement, null, null, DateTimeOffset.UnixEpoch));

        Assert.Equal("Stopped", card.GetProperty("group_state").GetString());
        Assert.Equal("unknown", card.GetProperty("image").GetString());
        Assert.False(card.GetProperty("always_on").GetBoolean());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("plan_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("plan_sites").ValueKind);
    }
    // #endregion site-shape
}
