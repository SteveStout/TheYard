using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// Which rows lift a card (ADR: One frame for every resource, the addendum of 1.0.4.14): a row that
/// states information reads info and leaves the card's tone alone, and a row that states a fault
/// reads warning with the count or the date that makes it one in its detail. One case a card the
/// review named, and the frame's sources: none is filled in, and the card's own list names each.
/// </summary>
public sealed partial class CardFrameTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A card's inputs: one minute of readings, one bucket of sums, one hour of facts and events.</summary>
    private static ResourceCardInputs Inputs(IReadOnlyDictionary<string, double?> minute, IReadOnlyDictionary<string, string?> facts, IReadOnlyList<PlanEvent>? events = null)
    {
        var bucket = new ResourceBucket(Noon.AddMinutes(-5), 5, minute, minute, minute, "test");
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Noon, new PlanHistoryAvailability(true, "kept in the test"),
            [new ResourceMinute(Noon.AddMinutes(-1), minute, "test")], [bucket], [bucket],
            [new ResourceHour(Noon.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/T/x", "tenant", facts, events ?? [])], null);
    }

    // #region card-row-tones
    [Fact]
    public void Paying_past_the_free_allowance_and_having_no_cap_are_information_on_the_insights_card()
    {
        var view = InsightsCard.View(Inputs(new Dictionary<string, double?> { ["ingest_today_gb"] = 1, ["ingest_month_gb"] = 9, ["sampling"] = 100, ["capped"] = 0 }, new Dictionary<string, string?> { ["workspace_name"] = "log-test" }));
        Assert.Equal("info", view.Rows.Single(r => r.Key == "cost").Tone);
        Assert.Equal("info", view.Rows.Single(r => r.Key == "cap").Tone);
        Assert.Equal("info", view.Readout.Lines.Single(l => l.Label == "Daily cap").Tone);
        // The empty value before the workspace's name is gone: one link, its words whole.
        Assert.Equal("sends to log-test", view.Rows.Single(r => r.Key == "reporters").Detail[0].Text);
    }

    [Fact]
    public void An_unplanned_event_is_information_on_the_mail_card_and_a_failed_pull_is_a_warning_with_its_count()
    {
        var mail = MailCard.View(Inputs(new Dictionary<string, double?>(), new Dictionary<string, string?>(),
            [new PlanEvent(Noon.AddMinutes(-30), "update", "Update domain", "acs-test", false)]));
        Assert.Equal("info", mail.Rows.Single(r => r.Key == "azure").Tone);

        var registry = AcrCard.View(Inputs(new Dictionary<string, double?> { ["pulls"] = 10, ["pulls_ok"] = 8, ["pushes"] = 1 }, new Dictionary<string, string?>()));
        var pulled = registry.Rows.Single(r => r.Key == "pulled");
        Assert.Equal("warning", pulled.Tone);
        Assert.Contains("2 failed", pulled.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_edge_is_information_until_its_retirement_day_and_a_warning_past_it_that_says_so()
    {
        var facts = new Dictionary<string, string?> { ["token"] = "set" };
        Assert.Equal("info", EdgeCard.View(Inputs(new Dictionary<string, double?>(), facts), true, "edge-test", new DateOnly(2026, 10, 30)).Rows.Single(r => r.Key == "needed").Tone);
        var past = EdgeCard.View(Inputs(new Dictionary<string, double?>(), facts), true, "edge-test", new DateOnly(2026, 10, 20)).Rows.Single(r => r.Key == "needed");
        Assert.Equal("warning", past.Tone);
        Assert.Contains("past its date, Oct 20, 2026", past.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stale_live_read_is_the_frame_s_unread_rule_not_a_warning_line()
    {
        var stale = Inputs(new Dictionary<string, double?> { ["dtu"] = 5 }, new Dictionary<string, string?>()) with { Now = Noon.AddHours(3) };
        Assert.Equal("info", ResourceView.LiveRead(stale).Tone);
        Assert.Equal(CardFrame.ThisProcess, ResourceView.LiveRead(stale).Source);
    }

    /// <summary>No default source: the frame fills none in, and the card's own list names a row or line by its key or label.</summary>
    [Fact]
    public void The_frame_fills_in_no_source_and_a_card_s_list_names_each_line()
    {
        var row = new CloudRow("throttling", "Is anything throttled?", "0 throttled", [], "good");
        var line = new CloudLine("Throttled", "0", "info");
        var report = new CloudReport("24h", ["24h"], true, null, 0, 30, "Cosmos DB", null, null, [], [], new CloudChart("", [], [], 90, "", 0), [row], new CloudReadout("The account", null, "", [line], ""));
        var completed = CardFrame.Complete(report, null);
        Assert.Null(completed.Rows[0].Source);
        Assert.Null(completed.Readout.Lines[0].Source);
        var sourced = CardFrame.Sourced(completed, new Dictionary<string, string>(StringComparer.Ordinal) { ["throttling"] = "Azure Monitor" });
        Assert.Equal("Azure Monitor", sourced.Rows[0].Source);
        Assert.Null(sourced.Readout.Lines[0].Source);
    }
    /// <summary>A tier or a sampling share the record does not state reads "not read", never a word or a hundred typed in the code.</summary>
    [Fact]
    public void A_tier_or_a_sampling_share_the_record_does_not_state_is_not_read()
    {
        var insights = InsightsCard.View(Inputs(new Dictionary<string, double?> { ["sampling"] = 100 }, new Dictionary<string, string?> { ["workspace_name"] = "log-test" }));
        Assert.StartsWith("not read", insights.Readout.Lines.Single(l => l.Label == "Sampling setting").Value, StringComparison.Ordinal);
        using var record = System.Text.Json.JsonDocument.Parse("""{"location":"global","properties":{"dataLocation":"United States"}}""");
        Assert.Equal("TIER NOT READ UNITED STATES", MailCard.Facts(record.RootElement, null)["spec"]);
        using var named = System.Text.Json.JsonDocument.Parse("""{"sku":{"name":"Standard"},"properties":{"dataLocation":"United States"}}""");
        Assert.Equal("STANDARD UNITED STATES", MailCard.Facts(named.RootElement, null)["spec"]);
    }
    // #endregion card-row-tones
}
