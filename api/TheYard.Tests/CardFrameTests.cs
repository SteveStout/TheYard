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
        foreach (string route in new[] { "/api/admin/plan", "/api/admin/cloud/sqldb", "/api/admin/cloud/cosmosdb", "/api/admin/cloud/registry", "/api/admin/cloud/insights", "/api/admin/cloud/mail", "/api/admin/cloud/edge" })
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
