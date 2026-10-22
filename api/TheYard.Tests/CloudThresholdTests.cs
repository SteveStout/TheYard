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
    public void The_sql_card_lists_the_database_it_reads_and_reads_its_size_from_the_record()
    {
        var view = SqlCard.View(Held("storage_gb", 1.8, new Dictionary<string, string?> { ["dtu"] = "5", ["max_gb"] = "2", ["served_by"] = "theyard.stevenstout.biz, on APP-THEYARD-ONE-SS" }));
        Assert.Equal(90, view.Dials[1].Percent);
        Assert.Equal("alarm", view.Dials[1].Tone);
        var databases = view.Rows[0].Table;
        Assert.Equal(["Database", "Type", "Size", "Read by this card"], databases.Columns);
        Assert.Single(databases.Rows);
        // What runs on it is this process's own settings, never a name written down.
        Assert.Equal("theyard.stevenstout.biz, on APP-THEYARD-ONE-SS", view.Rows.Single(r => r.Key == "runs").Answer);
    }


    [Fact]
    public void The_cosmos_card_reads_against_the_throughput_the_account_provisions_and_marks_every_throttled_bucket()
    {
        var view = CosmosCard.View(Held("ru_per_s", 900, new Dictionary<string, string?> { ["container_count"] = "17", ["throughput"] = "1000" }, new Dictionary<string, double?> { ["throttled"] = 4 }));
        Assert.Equal(90, view.Dials[0].Percent);
        Assert.Equal("alarm", view.Dials[0].Tone);
        Assert.Equal("warning", view.Rows.Single(r => r.Key == "throttling").Tone);
        Assert.Contains(view.Chart.Marks, mark => mark.Kind == "event" && mark.Title.Contains("429", StringComparison.Ordinal));
    }


    [Fact]
    public void The_free_tier_allowance_and_the_basic_read_rate_apply_only_while_the_record_says_so()
    {
        // The free tier's 25 GB and Basic's 1,000 reads a minute are the two ceilings no record
        // states; 1.0.4.0 measured every account and every registry against them (the review of
        // 1.0.4.0, finding 7). On: the dial reads against the ceiling. Off, or another tier: no
        // ceiling, the dial says so, and nothing is amber on a figure that does not apply.
        var free = CosmosCard.View(Held("storage_gb", 20, new Dictionary<string, string?> { ["free_tier"] = "on", ["throughput"] = "1000" }));
        Assert.Equal(80, free.Dials[1].Percent);
        Assert.Contains("of 25 GB", free.Dials[1].Absolute, StringComparison.Ordinal);
        var paid = CosmosCard.View(Held("storage_gb", 20, new Dictionary<string, string?> { ["free_tier"] = "off", ["throughput"] = "1000" }));
        Assert.Null(paid.Dials[1].Percent);
        Assert.Contains("no free-tier allowance", paid.Dials[1].Absolute, StringComparison.Ordinal);
        var basic = AcrCard.View(Held("pulls", 900, new Dictionary<string, string?> { ["tier"] = "Basic", ["size_limit_gb"] = "10" }));
        Assert.Equal(90, basic.Dials[1].Percent);
        var standard = AcrCard.View(Held("pulls", 900, new Dictionary<string, string?> { ["tier"] = "Standard", ["size_limit_gb"] = "100" }));
        Assert.Null(standard.Dials[1].Percent);
        Assert.Contains("not read", standard.Dials[1].Absolute, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_card_names_the_untagged_layers_and_never_purges_them()
    {
        var view = AcrCard.View(Held("storage_gb", 9.5, new Dictionary<string, string?> { ["untagged"] = "12", ["untagged_gb"] = "3.10", ["size_limit_gb"] = "10" }));
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
        // An hourly source keeps one document an hour, so its newest reading speaks at once: the
        // live workspace read 0.098 of its 0.1 GB cap on 18 October, and that is red, not information.
        var nearCap = InsightsCard.View(Held("ingest_today_gb", 0.098, new Dictionary<string, string?> { ["cap_gb"] = "0.1" }, minutes: 1));
        Assert.Equal("alarm", nearCap.Dials[0].Tone);
        Assert.Equal("alarm", nearCap.Rows.Single(r => r.Key == "cap").Tone);
        // No cap set is information, not a fault: the dial says when ingestion is too much.
        Assert.Equal("info", InsightsCard.View(Held("ingest_today_gb", 0.2, new Dictionary<string, string?>())).Rows.Single(r => r.Key == "cap").Tone);
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
        var view = EdgeCard.View(Held("credits", 270), configured: false, "theyard-edge", new DateOnly(2026, 10, 30));
        Assert.False(view.Available);
        Assert.Equal("NETLIFY_TOKEN is not set, so the edge is not read.", view.Note);
        Assert.Null(view.Resource!.Href);
        Assert.Equal("alarm", EdgeCard.View(Held("credits", 270), configured: true, "theyard-edge", new DateOnly(2026, 10, 30)).Dials[0].Tone);
        Assert.StartsWith("until Oct 30", view.Rows.Single(r => r.Key == "needed").Answer, StringComparison.Ordinal);
        // A token that answers but cannot see the site is a sentence too, as the first live roll found.
        var unseen = EdgeCard.View(Held("credits", 0, new Dictionary<string, string?> { ["token"] = "set", ["site"] = "the token sees 0 sites and none is theyard-edge" }), configured: true, "theyard-edge", new DateOnly(2026, 10, 30));
        Assert.Equal("NETLIFY_TOKEN is set, but the token sees 0 sites and none is theyard-edge, so the edge is not read.", unseen.Note);
    }

    /// <summary>
    /// The cache dial reads the newest hour's per-address statuses, the sample its own detail
    /// prints, never a kept minute an hour behind it: a minute that still says 0 under a sample
    /// of four hits reads 100 and green (ADR: One frame for every resource, the addendum on one
    /// sample for the dial and the detail).
    /// </summary>
    [Fact]
    public void The_edge_cache_dial_reads_the_sample_its_detail_prints()
    {
        const string fourHits = "/assets/index-x.js \"Netlify Edge\"; hit; ttl=31536000 · /og.png \"Netlify Edge\"; hit; ttl=86400 · /api/facets \"Netlify Edge\"; hit; ttl=15 · /api/docs/readme \"Netlify Edge\"; hit; ttl=86400";
        var facts = new Dictionary<string, string?> { ["token"] = "set", ["sampled"] = "/assets/index-x.js, /og.png, /api/facets, /api/docs/readme", ["cache_status"] = fourHits };
        var view = EdgeCard.View(Held("cache_hit", 0, facts), configured: true, "theyard-edge", new DateOnly(2026, 10, 30));

        var dial = view.Dials.Single(d => d.Name == "Cache hits");
        Assert.Equal(100.0, dial.Percent);
        Assert.Equal("good", dial.Tone);
        Assert.Equal("good", view.Rows.Single(r => r.Key == "cached").Tone);
        Assert.StartsWith("100 %", view.Rows.Single(r => r.Key == "cached").Answer, StringComparison.Ordinal);
        // The sparkline's newest value and the chart's newest point are the dial's sample too.
        Assert.Equal(dial.Percent, view.Sparks.Single(spark => spark.Name.StartsWith("Cache hits", StringComparison.Ordinal)).Values[^1]);
        Assert.Equal(dial.Percent, view.Chart.Series.Single(series => series.Name == "Cache hits").Points[^1].Value);
        // Before any sample, the kept minutes stand as they are.
        var unsampled = EdgeCard.View(Held("cache_hit", 25), configured: true, "theyard-edge", new DateOnly(2026, 10, 30));
        Assert.Equal(25.0, unsampled.Sparks.Single(spark => spark.Name.StartsWith("Cache hits", StringComparison.Ordinal)).Values[^1]);

        // An address that did not answer is left out; one with no Cache-Status is a miss.
        Assert.Equal(((double?)50.0, 2), NetlifyEdge.ShareOf("/a \"Netlify Edge\"; hit · /b no Cache-Status · /c did not answer"));
        Assert.Equal(((double?)null, 0), NetlifyEdge.ShareOf(null));
    }
    /// <summary>
    /// An hourly card's hour is asked before its minute, so a minute that carries what the hour
    /// sampled carries this hour's sample, not the one before (the edge's cache share).
    /// </summary>
    [Fact]
    public async Task An_hourly_card_s_minute_carries_the_sample_its_own_hour_took()
    {
        double? sample = 0;
        var card = new ResourceCard("edge", "edge", "theyard-edge", ["cache_hit"],
            (_, now, version, _) => Task.FromResult<IReadOnlyList<ResourceMinute>>([new ResourceMinute(now, new Dictionary<string, double?> { ["cache_hit"] = sample }, version)]),
            (hour, _, _) =>
            {
                sample = 100;
                return Task.FromResult(new ResourceHour(hour, "", null, new Dictionary<string, string?>(), []));
            },
            inputs => EdgeCard.View(inputs, configured: true, "theyard-edge", new DateOnly(2026, 10, 30)), Hourly: true);
        var keep = new MemoryResourceHistory("kept in the test");
        var recorder = new CloudRecorder([(card, keep)], new BuildInfo("test", "test"), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudRecorder>.Instance);

        await recorder.RecordOnceAsync("edge", CancellationToken.None);

        Assert.Equal(100.0, (await keep.NewestAsync(1, CancellationToken.None)).Single().Values["cache_hit"]);
    }
    /// <summary>
    /// One tone rule for information (ADR: One frame for every resource, the addendum of 1.0.4.23): the
    /// identity holding no role that runs a query is information on the Insights card as it is on the
    /// registry and nightly cards; the mail card's Azure row alarms on an unplanned event the way SQL's
    /// and the plan's do; and a tier the record does not state leaves the spec line and is said on a
    /// readout line in a sentence.
    /// </summary>
    [Fact]
    public void Information_never_lifts_a_card_and_an_unplanned_event_on_mail_does()
    {
        var refused = InsightsCard.View(Held("ingest_today_gb", 0.01, new Dictionary<string, string?> { ["query"] = "refused" }));
        Assert.Equal("info", refused.Rows.Single(r => r.Key == "dropped").Tone);

        var quiet = Held("sends", 1, new Dictionary<string, string?>());
        var restarted = quiet with { Hours = [quiet.Hours[0] with { Events = [new PlanEvent(Now.AddMinutes(-30), "restart", "Restart", "acs-test", Planned: false)] }] };
        Assert.Equal("alarm", MailCard.View(restarted).Rows.Single(r => r.Key == "azure").Tone);
        Assert.Equal("good", MailCard.View(quiet).Rows.Single(r => r.Key == "azure").Tone);

        Assert.Equal("tier not stated by the record", MailCard.View(quiet).Readout.Lines.Single(l => l.Label == "Tier").Value);
        Assert.Equal("Standard", MailCard.View(Held("sends", 1, new Dictionary<string, string?> { ["tier"] = "Standard" })).Readout.Lines.Single(l => l.Label == "Tier").Value);
    }

    /// <summary>The recorder's log line says which read failed: the hour's when the hour threw, else the minute's.</summary>
    [Fact]
    public void A_failed_read_names_its_part_and_an_unkept_card_is_asked_once_a_minute()
    {
        var hour = new HttpRequestException("refused");
        hour.Data[CloudRecorder.PartKey] = "hour";
        Assert.Equal("hour", CloudRecorder.PartOf(hour));
        Assert.Equal("minute", CloudRecorder.PartOf(new HttpRequestException("refused")));

        var reader = new CloudReader([], null, (_, _) => Task.FromResult<double?>(null), TimeProvider.System);
        Assert.True(reader.Due("nightly", Now));
        Assert.False(reader.Due("nightly", Now.AddSeconds(30)));
        Assert.True(reader.Due("nightly", Now.AddSeconds(61)));
        Assert.True(reader.Due("insights", Now.AddSeconds(30)));
    }
    // #endregion cloud-threshold-tests
}
