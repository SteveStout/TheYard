using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Tests;

// #region card-frame-tests
/// <summary>
/// The CardShell's slots (ADR: The onion cut into vertical slices, the CardShell): one tone rule in
/// Domain that every part of a card reads, the same slots on every card the API answers, and a
/// source on every summary line and row.
/// </summary>
public sealed partial class CardFrameTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(new[] { 0, 2 }, 30L, 60, CardTone.Red)]
    [InlineData(new[] { 1, 0 }, 30L, 60, CardTone.Amber)]
    [InlineData(new[] { 0, 0 }, 30L, 60, CardTone.Fine)]
    [InlineData(new[] { 0, 0 }, 180L, 60, CardTone.Fine)]
    [InlineData(new[] { 0, 0 }, 181L, 60, CardTone.Unread)]
    [InlineData(new[] { 0, 0 }, 3_000L, 3_600, CardTone.Fine)]
    [InlineData(new[] { 2, 0 }, 99_999L, 60, CardTone.Red)]
    public void The_tone_is_red_over_red_amber_over_amber_unread_when_old_and_fine_otherwise(int[] readings, long age, int cadence, CardTone expected) =>
        Assert.Equal(expected, CardTones.Of(readings, age, cadence));

    [Fact]
    public void Nothing_read_is_unread() => Assert.Equal(CardTone.Unread, CardTones.Of([], null, 60));

    [Fact]
    public void The_chips_name_each_dial_s_lines_and_the_hold()
    {
        var dial = new CloudDial("CPU, right now", 4, "4 %", "0.08 of 2 vCPU", 60, 80, 20, 30, "good");
        Assert.Equal(["CPU amber 60 %", "CPU red 80 %", $"held {PlanThresholds.Sustained} min"], CardFrame.Chips([dial], held: true).Select(chip => chip.Label));
        Assert.Equal(["amber", "red"], CardFrame.Chips([dial], held: false).Select(chip => chip.Tone));
    }

    [Fact]
    public void A_warning_or_alarm_row_lifts_the_card_s_tone_the_way_a_dial_over_its_line_does()
    {
        // The dials say fine; the throttling row says warning (any 429 is amber). The card is amber,
        // and the rail's dot with it. 1.0.4.0 read the dials alone (the review of 1.0.4.0, finding 6).
        var dial = new CloudDial("RU/s, right now", 1, "1 %", "4 of 1,000 RU/s", 70, 90, 5, 10, "good");
        var row = new CloudRow("throttling", "Is anything throttled?", "731 throttled", [], "warning");
        var report = new CloudReport("24h", ["1h", "24h"], true, null, 0, 30, "Cosmos DB", null, null, [dial], [], new CloudChart("", [], [], 90, "", 0), [row], new CloudReadout("The account", null, "", [], ""));
        Assert.Equal("amber", CardFrame.Complete(report, "Azure Monitor", null).Tone);
        Assert.Equal("red", CardFrame.Complete(report with { Rows = [row with { Tone = "alarm" }] }, "Azure Monitor", null).Tone);
        Assert.Equal("fine", CardFrame.Complete(report with { Rows = [row with { Tone = "info" }] }, "Azure Monitor", null).Tone);
    }

    [Fact]
    public void The_page_reads_every_card_as_often_as_the_tone_rule_assumes()
    {
        string common = File.ReadAllText(Path.Combine(Repo.Root(), "src", "components", "admin", "shared", "common.tsx"));
        var match = RefreshMs().Match(common);
        Assert.True(match.Success, "common.tsx declares REFRESH_MS");
        Assert.Equal(CardFrame.RefreshSeconds * 1000, int.Parse(match.Groups[1].Value.Replace("_", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Every_card_answers_the_same_slots_and_every_line_and_row_names_its_source()
    {
        using var client = factory.CreateClient();
        string[] slots = ["title", "resource", "tone", "spec", "spec_source", "headline", "dials", "sparks", "thresholds", "chart", "rows", "readout", "as_of_ms", "read_age_seconds", "refresh_seconds"];
        foreach (string route in new[] { "/api/admin/plan", "/api/admin/cloud/sqldb", "/api/admin/cloud/cosmosdb", "/api/admin/cloud/registry", "/api/admin/cloud/insights", "/api/admin/cloud/mail", "/api/admin/cloud/edge", "/api/admin/cloud/nightly" })
        {
            using var card = JsonDocument.Parse(await client.GetStringAsync(route));
            var root = card.RootElement;
            Assert.All(slots, slot => Assert.True(root.TryGetProperty(slot, out _), $"{route} has no {slot}"));
            Assert.Contains(root.GetProperty("tone").GetString(), new[] { "fine", "amber", "red", "unread" });
            Assert.NotEmpty(root.GetProperty("thresholds").EnumerateArray());
            Assert.All(root.GetProperty("readout").GetProperty("lines").EnumerateArray(), line => Assert.False(string.IsNullOrEmpty(line.GetProperty("source").GetString()), $"{route}: {line.GetProperty("label")} names no source"));
            Assert.All(root.GetProperty("rows").EnumerateArray(), row => Assert.False(string.IsNullOrEmpty(row.GetProperty("source").GetString()), $"{route}: {row.GetProperty("key")} names no source"));
        }
    }

    [GeneratedRegex(@"export const REFRESH_MS = ([0-9_]+);")]
    private static partial Regex RefreshMs();
}
// #endregion card-frame-tests
