using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The registry card's image table (ADR: One frame for every resource, the addendum of 1.0.4.16):
/// one row an image with what it is, which web app runs it at which tag, its tags, its bytes and its
/// newest push; a warning for an image with no description and for the two images of one build
/// running different tags; and the per-image sums read from a recorded manifest list.
/// </summary>
public partial class CloudViewTests
{
    private const string Login = "crtest.azurecr.io";

    /// <summary>A manifest list as the registry's /acr/v1/{name}/_manifests answers it: two tagged, one untagged.</summary>
    private const string Manifests = """
        {"registry":"crtest.azurecr.io","imageName":"theyard","manifests":[
          {"digest":"sha256:a","imageSize":1073741824,"createdTime":"2026-10-20T10:00:00Z","lastUpdateTime":"2026-10-21T13:20:00.1234567Z","tags":["v1.0.4.10"]},
          {"digest":"sha256:b","imageSize":536870912,"createdTime":"2026-10-19T10:00:00Z","lastUpdateTime":"2026-10-20T03:00:00Z","tags":["v1.0.4.9","latest"]},
          {"digest":"sha256:c","imageSize":268435456,"createdTime":"2026-10-18T10:00:00Z","lastUpdateTime":"2026-10-18T10:00:00Z"}
        ]}
        """;

    private static PlanHour Plan(params (string App, string Image)[] apps) =>
        new(Now.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/serverfarms/plan-test", "tenant", "B2", 1, 2, 3.5,
            [.. apps.Select(app => new PlanAppState(app.App, $"/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/sites/{app.App}", "Running", true, null, "app,linux,container", app.Image))],
            [], "westus3", true, 3);

    /// <summary>The registry card's inputs from its facts, as its hour would keep them.</summary>
    private static ResourceCardInputs Registry(AcrHoldings? holdings, PlanHour? plan)
    {
        using var record = JsonDocument.Parse($$$"""{"sku":{"name":"Basic"},"location":"westus2","properties":{"loginServer":"{{{Login}}}"}}""");
        return Held("storage_gb", 1, AcrCard.Facts(record.RootElement, holdings, null, plan));
    }

    private static AcrHoldings Two() =>
        new([new AcrImage("theyard", 3, 1, 1_879_048_192, 268_435_456, Now.AddHours(-2)), new AcrImage("theyard-render", 2, 0, 268_435_456, 0, Now.AddHours(-3))]);

    // #region registry-table-tests
    [Fact]
    public void The_manifest_list_sums_to_one_image_s_tags_untagged_bytes_and_newest_push()
    {
        using var answer = JsonDocument.Parse(Manifests);
        var image = AcrCatalog.Image("theyard", answer.RootElement);
        Assert.Equal(("theyard", 3, 1), (image.Name, image.Tags, image.Untagged));
        Assert.Equal(1_879_048_192L, image.Bytes);
        Assert.Equal(268_435_456L, image.UntaggedBytes);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 13, 20, 0, 123, TimeSpan.Zero).AddTicks(4567), image.LastPush);
        using var empty = JsonDocument.Parse("{}");
        Assert.Equal(new AcrImage("x", 0, 0, 0, 0, null), AcrCatalog.Image("x", empty.RootElement));
        Assert.Equal((1, 268_435_456L), (new AcrHoldings([image]).UntaggedManifests, new AcrHoldings([image]).UntaggedBytes));
    }

    [Fact]
    public void Both_images_are_described_with_the_web_app_that_runs_each_at_its_live_tag()
    {
        var plan = Plan(("app-test", $"DOCKER|{Login}/theyard:v1.0.4.10"), ("app-render-test", $"DOCKER|{Login}/theyard-render:v1.0.4.10"));
        var row = AcrCard.View(Registry(Two(), plan)).Rows.Single(r => r.Key == "holds");
        Assert.Equal("good", row.Tone);
        Assert.Equal(AcrCard.ImageColumns, row.Table.Columns);
        var api = row.Table.Rows[0];
        Assert.Equal(["theyard", AcrCard.Descriptions["theyard"], "app-test", "v1.0.4.10", "3 · 1 untagged", "1.75 GB, 0.25 GB untagged"], api.Take(6).Select(cell => cell.Text));
        Assert.Equal("https://portal.azure.com/#@tenant/resource/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/sites/app-test", api[2].Href);
        Assert.EndsWith(" Central", api[6].Text, StringComparison.Ordinal);
        Assert.Equal(AcrCard.Descriptions["theyard-render"], row.Table.Rows[1][1].Text);
    }

    [Fact]
    public void An_image_with_no_description_is_information_and_says_where_one_goes()
    {
        var holdings = new AcrHoldings([.. Two().Images, new AcrImage("sidecar", 1, 0, 1024, 0, Now)]);
        var row = AcrCard.View(Registry(holdings, Plan())).Rows.Single(r => r.Key == "holds");
        Assert.Equal("info", row.Tone);
        Assert.Contains(row.Detail, d => d.Text.StartsWith("the table is the hourly catalogue, read ", StringComparison.Ordinal));
        Assert.Equal("no description", row.Table.Rows[2][1].Text);
        Assert.Contains(row.Detail, d => d.Text.StartsWith("sidecar has no description", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_images_of_one_build_on_different_tags_warn_and_name_both()
    {
        var plan = Plan(("app-test", $"DOCKER|{Login}/theyard:v1.0.4.11"), ("app-render-test", $"DOCKER|{Login}/theyard-render:v1.0.4.10"));
        var row = AcrCard.View(Registry(Two(), plan)).Rows.Single(r => r.Key == "holds");
        Assert.Equal("warning", row.Tone);
        Assert.Contains(row.Detail, d => d.Text == "theyard runs v1.0.4.11 and theyard-render runs v1.0.4.10: until they match, pages are drawn in the browser");
    }

    [Fact]
    public void An_image_no_web_app_runs_says_so_and_an_app_on_another_registry_is_not_counted()
    {
        var plan = Plan(("app-test", $"DOCKER|{Login}/theyard:v1.0.4.10"), ("app-elsewhere", "DOCKER|other.azurecr.io/theyard-render:v9"));
        var row = AcrCard.View(Registry(Two(), plan)).Rows.Single(r => r.Key == "holds");
        Assert.Equal(["no web app runs it", "none"], row.Table.Rows[1].Skip(2).Take(2).Select(cell => cell.Text));
        Assert.Equal("good", row.Tone);
        Assert.Equal(((string, string, string)?)("cr.azurecr.io", "theyard", "v1"), AcrCard.ImageOf("DOCKER|cr.azurecr.io/theyard:v1"));
        Assert.Null(AcrCard.ImageOf("NODE|20-lts"));
        Assert.Null(AcrCard.ImageOf(null));
    }

    [Fact]
    public void An_unread_catalogue_says_what_the_identity_needs_and_draws_no_table()
    {
        var row = AcrCard.View(Registry(null, null)).Rows.Single(r => r.Key == "holds");
        Assert.Empty(row.Table.Rows);
        Assert.Equal("the catalogue could not be read; AcrPull on the registry is what it needs", row.Detail.Single().Text);
        Assert.Equal("not read yet", AcrCard.View(Held("storage_gb", 1)).Rows.Single(r => r.Key == "holds").Answer);
    }
    /// <summary>The newest push in Central time, on both sides of each daylight-saving change: the hour itself, not only the suffix.</summary>
    [Theory]
    [InlineData("2026-03-08T07:30:00Z", "Mar 8 01:30 Central")]
    [InlineData("2026-03-08T08:30:00Z", "Mar 8 03:30 Central")]
    [InlineData("2026-11-01T06:30:00Z", "Nov 1 01:30 Central")]
    [InlineData("2026-11-01T07:30:00Z", "Nov 1 01:30 Central")]
    public void The_newest_push_is_the_central_hour_across_a_daylight_saving_change(string utc, string central)
    {
        var pushed = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);
        var row = AcrCard.View(Registry(new AcrHoldings([new AcrImage("theyard", 1, 0, 1024, 0, pushed)]), Plan())).Rows.Single(r => r.Key == "holds");
        Assert.Equal(central, row.Table.Rows[0][6].Text);
    }
    // #endregion registry-table-tests
}
