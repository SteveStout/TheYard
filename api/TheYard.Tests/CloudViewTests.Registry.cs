using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The registry card, read from GitHub's registry alone (ADR-009, the addendum on retiring the Azure registry): one
/// row an image with what it is, which web app runs it at which tag and its newest tag; whether an anonymous pull can
/// read every image, which a roll with no credential needs; whether the two images of one build run the same tag; and
/// a web app whose image is on another registry not counted as this one's.
/// </summary>
public partial class CloudViewTests
{
    private static PlanHour Plan(params (string App, string Image)[] apps) =>
        new(Now.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/serverfarms/plan-test", "tenant", "B2", 1, 2, 3.5,
            [.. apps.Select(app => new PlanAppState(app.App, $"/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/sites/{app.App}", "Running", true, null, "app,linux,container", app.Image))],
            [], "westus3", true, 3);

    /// <summary>Both images public with seven tags each, newest v1.0.4.10.</summary>
    private static IReadOnlyList<GhcrImage> Both(string api = "v1.0.4.10", string render = "v1.0.4.10") =>
        [new GhcrImage("theyard", true, 7, api), new GhcrImage("theyard-render", true, 7, render)];

    /// <summary>The two web apps on the version both images hold.</summary>
    private static PlanHour Rolled(string api = "v1.0.4.10", string render = "v1.0.4.10") =>
        Plan(("app-test", $"DOCKER|ghcr.io/stevestout/theyard:{api}"), ("app-render", $"DOCKER|ghcr.io/stevestout/theyard-render:{render}"));

    /// <summary>The registry card's inputs from its facts, as its hour would keep them.</summary>
    private static ResourceCardInputs Registry(IReadOnlyList<GhcrImage> images, PlanHour? plan) =>
        Held("unused", 0, RegistryCard.Facts(images, plan));

    [Fact]
    public void Both_images_are_described_with_the_web_app_that_runs_each_at_its_live_tag()
    {
        var row = RegistryCard.View(Registry(Both(), Rolled())).Rows.Single(r => r.Key == "holds");

        Assert.Equal(RegistryCard.ImageColumns, row.Table.Columns);
        Assert.Equal("2 images, 14 tags", row.Answer);
        Assert.Equal(["theyard", RegistryCard.Descriptions["theyard"], "app-test", "v1.0.4.10", "v1.0.4.10", "7"], row.Table.Rows[0].Select(cell => cell.Text));
        Assert.Equal("https://github.com/stevestout/TheYard/pkgs/container/theyard", row.Table.Rows[0][0].Href);
        Assert.Equal(RegistryCard.Descriptions["theyard-render"], row.Table.Rows[1][1].Text);
        Assert.Equal("info", row.Tone);
    }

    [Fact]
    public void An_image_with_no_description_is_information_and_says_where_one_goes()
    {
        var row = RegistryCard.View(Registry([.. Both(), new GhcrImage("sidecar", true, 1, "v1")], Rolled())).Rows.Single(r => r.Key == "holds");

        Assert.Equal("info", row.Tone);
        Assert.Contains(row.Detail, line => line.Text == "sidecar has no description: say what it is in RegistryCard.Descriptions");
        Assert.Equal("no description", row.Table.Rows[2][1].Text);
    }

    [Fact]
    public void Two_images_of_one_build_on_different_tags_warn_and_name_both()
    {
        var same = RegistryCard.View(Registry(Both(), Rolled(render: "v1.0.4.9"))).Rows.Single(r => r.Key == "same");

        Assert.Equal("warning", same.Tone);
        Assert.Equal("no: theyard runs v1.0.4.10 and theyard-render runs v1.0.4.9", same.Answer);
        Assert.Equal("good", RegistryCard.View(Registry(Both(), Rolled())).Rows.Single(r => r.Key == "same").Tone);
    }

    [Fact]
    public void An_image_no_web_app_runs_says_so_and_an_app_on_another_registry_is_not_counted()
    {
        var plan = Plan(("app-test", "DOCKER|ghcr.io/stevestout/theyard:v1.0.4.10"), ("app-elsewhere", "DOCKER|other.azurecr.io/theyard-render:v9"));
        var row = RegistryCard.View(Registry(Both(), plan)).Rows.Single(r => r.Key == "holds");

        Assert.Equal("no web app runs it", row.Table.Rows[1][2].Text);
        Assert.Equal("none", row.Table.Rows[1][3].Text);
        Assert.Equal(((string, string, string)?)("ghcr.io", "stevestout/theyard", "v1"), RegistryCard.ImageOf("DOCKER|ghcr.io/stevestout/theyard:v1"));
        Assert.Null(RegistryCard.ImageOf("NODE|20-lts"));
        Assert.Null(RegistryCard.ImageOf(null));
    }

    [Fact]
    public void An_image_an_anonymous_pull_cannot_read_is_red_because_a_roll_of_it_fails()
    {
        var view = RegistryCard.View(Registry([new GhcrImage("theyard", true, 7, "v1.0.4.10"), new GhcrImage("theyard-render", false, 0, null)], Rolled()));

        var row = view.Rows.Single(r => r.Key == "public");
        Assert.Equal("alarm", row.Tone);
        Assert.Equal("no: theyard-render refused an anonymous pull", row.Answer);
        Assert.Equal(("1 of 2", "alarm"), (view.Dials[0].Big, view.Dials[0].Tone));
        Assert.Equal("good", RegistryCard.View(Registry(Both(), Rolled())).Dials[0].Tone);
    }

    [Fact]
    public void A_web_app_behind_the_newest_tag_is_information_while_a_roll_is_under_way()
    {
        var view = RegistryCard.View(Registry(Both(api: "v1.0.4.11"), Rolled()));

        Assert.Equal(("1 of 2", "info"), (view.Dials[1].Big, view.Dials[1].Tone));
        Assert.Equal(("2 of 2", "good"), (RegistryCard.View(Registry(Both(), Rolled())).Dials[1].Big, RegistryCard.View(Registry(Both(), Rolled())).Dials[1].Tone));
    }

    [Fact]
    public void An_unread_registry_says_not_read_and_draws_no_table()
    {
        var view = RegistryCard.View(Held("unused", 0));

        Assert.Equal("not read yet", view.Rows.Single(r => r.Key == "holds").Answer);
        Assert.Empty(view.Rows.Single(r => r.Key == "holds").Table.Rows);
        Assert.All(view.Dials, dial => Assert.Equal(("not read", "info"), (dial.Big, dial.Tone)));
    }

    [Fact]
    public void The_card_costs_nothing_on_the_azure_bill_and_names_no_azure_registry()
    {
        var view = RegistryCard.View(Registry(Both(), Rolled()));

        Assert.Equal("nothing on the Azure bill", view.Rows.Single(r => r.Key == "cost").Answer);
        Assert.Equal("https://github.com/stevestout?tab=packages", view.Resource!.Href);
        Assert.DoesNotContain("azurecr", System.Text.Json.JsonSerializer.Serialize(view), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Which_web_app_runs_each_image_waits_on_the_plan_cards_hour()
    {
        var row = RegistryCard.View(Registry(Both(), null)).Rows.Single(r => r.Key == "holds");

        Assert.Contains(row.Detail, line => line.Text.StartsWith("which web app runs each image is read from the plan card's hour", StringComparison.Ordinal));
        Assert.All(row.Table.Rows, cells => Assert.Equal("no web app runs it", cells[2].Text));
    }

    [Fact]
    public void The_facts_name_the_images_their_public_count_and_the_anonymous_read()
    {
        var facts = RegistryCard.Facts(Both(), Rolled());

        Assert.Equal(("2", "2", "14"), (facts["images"], facts["public"], facts["tags"]));
        Assert.Equal("theyard v1.0.4.10, 7 tags · theyard-render v1.0.4.10, 7 tags", facts[GhcrPackages.Fact]);
        Assert.Equal("GHCR.IO PUBLIC PACKAGES", facts["spec"]);
        Assert.Null(RegistryCard.Facts(Both(), null)["apps_read"]);
    }

    [Fact]
    public void A_registry_with_a_kept_hour_and_no_minute_is_read_and_judged_good_by_that_hour()
    {
        // The registry keeps no minute (ADR-102, the addendum on the honesty rule): read by minutes, its hour never counted,
        // and on 19 November it sat unread for eight hours with a fresh hour kept, both dials information at 2 of 2.
        var kept = Registry(Both(), Rolled()) with { LastHour = [], Hours = [new ResourceHour(Now.AddMinutes(-30), "", null, RegistryCard.Facts(Both(), Rolled()), [])] };

        var card = CardFrame.Complete(RegistryCard.View(kept), RegistryCard.Anonymous, hourly: true);

        Assert.Equal(1800, card.ReadAgeSeconds);
        Assert.Null(card.Note);
        Assert.Equal("fine", card.Tone);
        Assert.All(card.Dials, dial => Assert.Equal("good", dial.Tone));
        Assert.Equal("30 min ago", card.Readout.Lines.Single(line => line.Label == "Hourly read").Value);
    }

    [Fact]
    public void A_registry_with_no_hour_kept_stays_unread_and_says_so()
    {
        var empty = Registry(Both(), Rolled()) with { LastHour = [], Hours = [] };

        var card = CardFrame.Complete(RegistryCard.View(empty), RegistryCard.Anonymous, hourly: true);

        Assert.Null(card.ReadAgeSeconds);
        Assert.Equal("unread", card.Tone);
        Assert.StartsWith("no hour kept yet", card.Note, StringComparison.Ordinal);
        Assert.Equal("not read yet: no hour kept yet", card.Readout.Lines.Single(line => line.Label == "Hourly read").Value);
    }
}
