using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// Where each resource card turns amber and red (ADR: One frame for every resource): every
/// number CloudThresholds holds, and each card's dial and rows held to them with fifteen minutes
/// of fixed readings; a capped hour on the telemetry workspace is red at once.
/// </summary>
public class CloudThresholdTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A card's inputs with one reading held for fifteen minutes and an hour of facts.</summary>
    private static ResourceCardInputs Held(string reading, double value, IReadOnlyDictionary<string, string?>? facts = null, IReadOnlyDictionary<string, double?>? sums = null, int minutes = 15)
    {
        var lastHour = Enumerable.Range(0, minutes)
            .Select(i => new ResourceMinute(Now.AddMinutes(i - minutes), new Dictionary<string, double?> { [reading] = value }, "1.0.3.171"))
            .ToList();
        var bucket = new ResourceBucket(Now.AddMinutes(-15), minutes, new Dictionary<string, double?> { [reading] = value }, new Dictionary<string, double?> { [reading] = value }, sums ?? new Dictionary<string, double?>(), "1.0.3.171");
        var hours = facts is null ? new List<ResourceHour>() : [new ResourceHour(Now.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/T/x", "tenant", facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), lastHour, [bucket], [bucket], hours, 1.23);
    }

    // #region cloud-threshold-tests
    [Fact]
    public void Every_cards_lines_are_the_numbers_the_record_names()
    {
        Assert.Equal((60d, 90d), CloudThresholds.SqlDtu);
        Assert.Equal((70d, 85d), CloudThresholds.SqlSpace);
        Assert.Equal((70d, 90d), CloudThresholds.CosmosThroughput);
        Assert.Equal((70d, 90d), CloudThresholds.CosmosStorage);
        Assert.Equal((70d, 90d), CloudThresholds.RegistryStorage);
        Assert.Equal((60d, 90d), CloudThresholds.RegistryPulls);
        Assert.Equal((70d, 90d), CloudThresholds.InsightsIngestion);
        Assert.Equal((50d, 90d), CloudThresholds.InsightsDropped);
        Assert.Equal((60d, 85d), CloudThresholds.MailSends);
        Assert.Equal((5d, 10d), CloudThresholds.MailBounces);
        Assert.Equal((60d, 85d), CloudThresholds.EdgeCredits);
        Assert.Equal((50d, 80d), CloudThresholds.EdgeMisses);
    }

    [Theory]
    [InlineData(30, "good")]
    [InlineData(59, "info")]
    [InlineData(60, "warning")]
    [InlineData(90, "alarm")]
    public void The_sql_card_turns_at_sixty_and_ninety_per_cent_DTU(double dtu, string tone) =>
        Assert.Equal(tone, SqlCard.View(Held("dtu", dtu, new Dictionary<string, string?> { ["dtu"] = "5", ["max_gb"] = "2" })).Dials[0].Tone);


    [Fact]
    public void The_sql_card_names_the_paused_free_database_and_reads_its_size_from_the_record()
    {
        var view = SqlCard.View(Held("storage_gb", 1.8, new Dictionary<string, string?> { ["dtu"] = "5", ["max_gb"] = "2", ["paused"] = "sqldb-theyard-ss" }));
        Assert.Equal(90, view.Dials[1].Percent);
        Assert.Equal("alarm", view.Dials[1].Tone);
        Assert.Contains("sqldb-theyard-ss is named, not read", view.Rows[0].Detail[0].Text);
    }


    [Fact]
    public void The_cosmos_card_reads_against_the_free_thousand_and_marks_every_throttled_bucket()
    {
        var view = CosmosCard.View(Held("ru_per_s", 900, new Dictionary<string, string?> { ["container_count"] = "17" }, new Dictionary<string, double?> { ["throttled"] = 4 }));
        Assert.Equal(90, view.Dials[0].Percent);
        Assert.Equal("alarm", view.Dials[0].Tone);
        Assert.Equal("warning", view.Rows.Single(r => r.Key == "throttling").Tone);
        Assert.Contains(view.Chart.Marks, mark => mark.Kind == "event" && mark.Title.Contains("429", StringComparison.Ordinal));
    }


    [Fact]
    public void The_registry_card_names_the_untagged_layers_and_never_purges_them()
    {
        var view = AcrCard.View(Held("storage_gb", 9.5, new Dictionary<string, string?> { ["untagged"] = "12", ["untagged_gb"] = "3.10" }));
        Assert.Equal("alarm", view.Dials[0].Tone);
        var stale = view.Rows.Single(r => r.Key == "stale");
        Assert.Equal("12 untagged manifests", stale.Answer);
        Assert.Contains("does not run it", stale.Detail[0].Text);
    }


    [Fact]
    public void A_capped_hour_on_the_insights_card_is_red_because_its_data_is_lost()
    {
        var facts = new Dictionary<string, string?> { ["cap_gb"] = "1", ["cap_from"] = "the workspace" };
        Assert.Equal("good", InsightsCard.View(Held("ingest_today_gb", 0.2, facts)).Rows.Single(r => r.Key == "dropped").Tone);
        var capped = InsightsCard.View(Held("ingest_today_gb", 0.95, facts, new Dictionary<string, double?> { ["capped"] = 1 }));
        Assert.Equal("alarm", capped.Rows.Single(r => r.Key == "dropped").Tone);
        Assert.Equal("alarm", capped.Dials[0].Tone);
        Assert.Contains(capped.Chart.Marks, mark => mark.Title == "The daily cap stopped collection");
        Assert.Equal("warning", InsightsCard.View(Held("ingest_today_gb", 0.2, new Dictionary<string, string?>())).Rows.Single(r => r.Key == "cap").Tone);
    }


    [Theory]
    [InlineData(4, "good")]
    [InlineData(5, "warning")]
    [InlineData(10, "alarm")]
    public void The_mail_card_turns_at_five_and_ten_per_cent_bounced(double bounced, string tone)
    {
        var sums = new Dictionary<string, double?> { ["delivered"] = 100 - bounced, ["bounced"] = bounced };
        Assert.Equal(tone, MailCard.View(Held("sends", 1, new Dictionary<string, string?>(), sums)).Dials[1].Tone);
    }


    [Fact]
    public void The_edge_card_says_in_a_sentence_when_there_is_no_token_and_names_its_own_retirement()
    {
        var view = EdgeCard.View(Held("credits", 270), configured: false);
        Assert.False(view.Available);
        Assert.Equal("NETLIFY_TOKEN is not set, so the edge is not read.", view.Note);
        Assert.Equal(EdgeCard.ProjectUrl, view.Resource!.Href);
        Assert.Equal("alarm", EdgeCard.View(Held("credits", 270), configured: true).Dials[0].Tone);
        Assert.StartsWith("until Oct 30", view.Rows.Single(r => r.Key == "needed").Answer, StringComparison.Ordinal);
        // A token that answers but cannot see the site is a sentence too, as the first live roll found.
        var unseen = EdgeCard.View(Held("credits", 0, new Dictionary<string, string?> { ["token"] = "set", ["site"] = "the token sees 0 sites and none is theyard-edge" }), configured: true);
        Assert.Equal("NETLIFY_TOKEN is set, but the token sees 0 sites and none is theyard-edge, so the edge is not read.", unseen.Note);
    }
    // #endregion cloud-threshold-tests
}
