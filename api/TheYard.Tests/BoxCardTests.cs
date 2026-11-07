using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The box's card (ADR: The box reports itself): eight rows that wait in info before the first snapshot, the three
/// minutes of silence that turn the reporting row red only when this process is older than that, the Sunshine unit
/// that is known to be off, a failed array member, a sensor at its own mark, a full file system, a stopped service
/// that must run, every row and line sourced, and no address and no port anywhere on the wire.
/// </summary>
public sealed class BoxCardTests
{
    private static readonly DateTimeOffset Nine = BoxSamples.Nine;

    private static ResourceCardInputs Inputs(bool kept)
    {
        var values = BoxSource.Values(BoxSamples.Snapshot());
        var lastHour = Enumerable.Range(0, 30).Select(i => new ResourceMinute(Nine.AddMinutes(i - 31), values, "test")).ToList();
        var bucket = new ResourceBucket(Nine.AddMinutes(-5), 5, values, values, values, "test");
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Nine, new PlanHistoryAvailability(true, "kept in the test"), kept ? lastHour : [], [bucket], [bucket], [], null);
    }

    private static (BoxSource Source, MemoryBoxSnapshots Snapshots) Source(TimeSpan processAge, BoxSnapshot? snapshot = null)
    {
        var snapshots = new MemoryBoxSnapshots();
        if (snapshot is not null)
        {
            snapshots.Accept(snapshot);
        }
        var start = new TheYard.Api.Shared.Instance.HostStart(new BoxSamples.Clock(Nine - processAge));
        return (new BoxSource(snapshots, new MemoryBoxLive(), start, new BoxSamples.Clock(Nine)), snapshots);
    }

    private static CloudRow Row(CloudReport report, string key) => report.Rows.Single(row => row.Key == key);

    [Fact]
    public void Before_the_first_snapshot_every_row_waits_in_info_and_none_shows_a_sample_value()
    {
        var report = BoxCard.View(Inputs(kept: false), Source(TimeSpan.FromMinutes(1)).Source);

        Assert.Equal(8, report.Rows.Count);
        Assert.All(report.Rows, row => Assert.Equal(("info", BoxSource.Waiting), (row.Tone, row.Answer)));
        Assert.Equal(BoxSource.Waiting, report.Spec);
        Assert.Equal(new CloudLink("WickedAwsome", BoxCard.Console), report.Resource);
    }

    [Fact]
    public void Three_minutes_of_silence_turns_the_reporting_row_red_but_never_during_this_processs_own_first_three()
    {
        var silentLong = BoxCard.View(Inputs(kept: false), Source(TimeSpan.FromMinutes(10), BoxSamples.Snapshot(Nine.AddMinutes(-4))).Source);
        var justRolled = BoxCard.View(Inputs(kept: false), Source(TimeSpan.FromMinutes(2)).Source);
        var neverPosted = BoxCard.View(Inputs(kept: false), Source(TimeSpan.FromMinutes(5)).Source);
        var reporting = BoxCard.View(Inputs(kept: true), Source(TimeSpan.FromHours(2), BoxSamples.Snapshot()).Source);

        Assert.Equal("alarm", Row(silentLong, "reporting").Tone);
        Assert.StartsWith("no: silent for 4 min", Row(silentLong, "reporting").Answer, StringComparison.Ordinal);
        Assert.Equal("info", Row(justRolled, "reporting").Tone);
        Assert.Equal("alarm", Row(neverPosted, "reporting").Tone);
        Assert.Equal("good", Row(reporting, "reporting").Tone);
        Assert.Contains("30 snapshots kept in the last hour", Row(reporting, "reporting").Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quiet_box_reads_fine_with_sunshine_known_to_be_off()
    {
        var report = BoxCard.View(Inputs(kept: true), Source(TimeSpan.FromHours(2), BoxSamples.Snapshot()).Source);

        Assert.All(report.Rows, row => Assert.Equal("good", row.Tone));
        Assert.Equal("6 of 8 active", Row(report, "services").Answer);
        Assert.Contains("next Sun 06:00", JsonSerializer.Serialize(Row(report, "services").Table), StringComparison.Ordinal);
        Assert.Equal("4 cores", Row(report, "cores").Answer);
        Assert.Equal("active raid0, 6 of 6 active", Row(report, "disks").Answer);
        Assert.Equal("41 °C coretemp Package id 0", Row(report, "temps").Answer);
        Assert.Equal("Linux Mint 22.3 · 4 cores · 31.3 GiB · 2 x RTX 2080 7.6 GiB · up 3 d 4 h", report.Spec);
    }

    [Fact]
    public void A_failed_member_a_sensor_at_its_mark_a_full_file_system_and_a_stopped_service_each_read_red()
    {
        var snapshot = BoxSamples.Snapshot();
        BoxSnapshot With(Func<BoxSnapshot, BoxSnapshot> change) => change(snapshot);
        CloudReport View(BoxSnapshot changed) => BoxCard.View(Inputs(kept: true), Source(TimeSpan.FromHours(2), changed).Source);

        var failed = View(With(s => s with { Disks = [s.Disks[0] with { RaidState = "active raid0 sdb1[1](F)", MembersActive = 5 }, s.Disks[1]] }));
        var hot = View(With(s => s with { Temps = [new("coretemp Package id 0", 82, 82, 100)] }));
        var warm = View(With(s => s with { Temps = [new("coretemp Package id 0", 75, 82, 100)] }));
        var full = View(With(s => s with { Disks = [s.Disks[0] with { UsedB = 2_600_000_000_000 }] }));
        var stopped = View(With(s => s with { Services = [.. s.Services.Where(service => service.Unit != "jdrive.service")] }));
        var morning = View(With(s => s with { Services = [.. s.Services.Select(service => service.Unit == BoxRows.MorningRun ? service with { Note = "last result exit-code" } : service)] }));
        var swapping = View(With(s => s with { Swap = new(2_100_000_000, 1_000_000) }));

        Assert.Equal("alarm", Row(failed, "disks").Tone);
        Assert.Equal("alarm", Row(hot, "temps").Tone);
        Assert.Equal("warning", Row(warm, "temps").Tone);
        Assert.Equal("alarm", Row(full, "disks").Tone);
        Assert.Equal("alarm", Row(stopped, "services").Tone);
        Assert.Contains("jdrive.service", Row(stopped, "services").Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("warning", Row(morning, "services").Tone);
        Assert.Equal("warning", Row(swapping, "memory").Tone);
    }

    [Fact]
    public void Every_row_and_line_names_its_source_and_no_address_or_port_reaches_the_wire()
    {
        var snapshot = BoxSamples.Snapshot() with
        {
            Services = [.. BoxSamples.Snapshot().Services.Select(service => service.Unit == "tailscaled.service" ? service with { Note = "198.51.100.7 listening on port 41641" } : service)],
            Disks = [.. BoxSamples.Snapshot().Disks.Select(disk => disk with { Device = "192.0.2.10:/export" })],
        };
        var report = BoxCard.View(Inputs(kept: true), Source(TimeSpan.FromHours(2), snapshot).Source);
        string wire = JsonSerializer.Serialize(report);

        Assert.All(report.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
        Assert.All(report.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), line.Label));
        Assert.DoesNotMatch(new Regex(@"\b\d{1,3}(\.\d{1,3}){3}\b"), wire);
        Assert.DoesNotContain("41641", wire, StringComparison.Ordinal);
        Assert.Contains("an address", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_source_keeps_one_minute_a_snapshot_and_its_hour_says_what_the_box_is()
    {
        var (source, snapshots) = Source(TimeSpan.FromHours(2));
        var waiting = await source.HourAsync(Nine, Nine.AddHours(-1), CancellationToken.None);
        snapshots.Accept(BoxSamples.Snapshot(Nine.AddMinutes(-1)));
        snapshots.Accept(BoxSamples.Snapshot(Nine));

        var minutes = await source.MinutesAsync(Nine.AddMinutes(-5), Nine, "1.0.4.0", CancellationToken.None);
        var hour = await source.HourAsync(Nine, Nine.AddHours(-1), CancellationToken.None);

        Assert.Equal(BoxSource.Waiting, waiting.Facts["box"]);
        Assert.Equal(2, minutes.Count);
        Assert.Equal(Math.Round((26.8 + 30.9 + 28.1 + 27.1) / 4, 2), minutes[0].Values["cpu"]);
        Assert.Equal(25.3, minutes[0].Values["memory"]);
        Assert.Equal(0.66, minutes[0].Values["disk_root"]);
        Assert.Equal(17_710, minutes[0].Values["net_in_bps"]);
        Assert.Empty(await source.MinutesAsync(Nine.AddMinutes(-5), Nine, "1.0.4.0", CancellationToken.None));
        Assert.Equal("Linux Mint 22.3, kernel 6.14.0-35-generic", hour.Facts["box"]);
        Assert.Equal("active raid0, 6 of 6 active", hour.Facts["raid"]);
    }
}
