using System.Text.Json;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The merger's health reading (MergerHealth): counts and ages from the two spool folders and
/// merge.log's write time, and nothing else, because the Admin tab's nightly card shows it to the
/// public (ADR: The tracker merger runs in Azure).
/// </summary>
public sealed class MergerHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 16, 0, 0, TimeSpan.Zero);

    private static MergeEntry Waited(string name, int minutes, int seconds = 0) =>
        new(name, Now.AddMinutes(-minutes).AddSeconds(-seconds), 120);

    [Fact]
    public void An_empty_spool_is_keeping_up_and_says_no_oldest_wait()
    {
        var health = MergerHealth.Measure([], Now.AddSeconds(-40), Now);
        Assert.Equal(new MergerHealth(40, 0, 0, 0, null), health);
    }

    [Fact]
    public void Waits_are_counted_past_five_and_past_ten_minutes_and_the_oldest_is_named_in_seconds()
    {
        var health = MergerHealth.Measure([Waited("a.csv", 1), Waited("b.csv", 5, 1), Waited("c.csv", 11), Waited("d.csv", 5)], null, Now);
        Assert.Null(health.LogAgeSeconds);
        Assert.Equal(4, health.Waiting);
        Assert.Equal(2, health.WaitingOver5);
        Assert.Equal(1, health.WaitingOver10);
        Assert.Equal(660, health.OldestWaitSeconds);
    }

    [Fact]
    public void A_file_stamped_ahead_of_the_clock_waits_nothing()
    {
        var health = MergerHealth.Measure([new MergeEntry("a.csv", Now.AddSeconds(30), 1)], Now.AddSeconds(5), Now);
        Assert.Equal(0, health.OldestWaitSeconds);
        Assert.Equal(0, health.LogAgeSeconds);
    }

    [Fact]
    public async Task The_reading_counts_lane_files_in_both_spools_and_skips_the_merger_s_own_files()
    {
        using var s = new Scratch();
        s.Spool("20261022-150000Z-lane-a.csv", "Ann,Acme,Email,10/22/2026,,x\r\n");
        s.Spool("moveout-lane-x.txt", "# approved-by: Steve\r\n5\r\n");
        s.Spool("notes.txt", "not a lane file\r\n");
        s.Write("inbox/tracker-inbox/20261022-150100Z-cloud-b.csv", "Bea,Beta,Email,10/22/2026,,y\r\n");
        s.Write("tracker-inbox/merged/20261021-old.csv", "old\r\n");
        s.Write(MergePaths.Log, "2026-10-22 10:59:00 MERGED x\r\n");

        var health = await MergerHealth.ReadAsync(s.Store, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(3, health.Waiting);
        Assert.NotNull(health.LogAgeSeconds);
        Assert.InRange(health.LogAgeSeconds!.Value, 30, 300);
    }

    [Fact]
    public void The_answer_on_the_wire_is_five_numbers_under_snake_case_names_and_nothing_else()
    {
        string json = JsonSerializer.Serialize(new MergerHealth(12, 3, 1, 0, 400));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "log_age_s", "waiting", "waiting_over_5", "waiting_over_10", "oldest_wait_s" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.All(doc.RootElement.EnumerateObject(), p => Assert.Equal(JsonValueKind.Number, p.Value.ValueKind));
    }
}
