using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The box's rows' tables (ADR: The box reports itself): each core with its clock and temperature, the memory pools,
/// the interfaces by name with the loopback left out, each file system with the array's state, each sensor with its
/// own marks, and each service with how long it has been up; and the reporting row's edges, the newest snapshot held
/// or kept, whichever is later, and the hour the recorder keeps.
/// </summary>
public sealed class BoxRowsTests
{
    private static readonly DateTimeOffset Nine = BoxSamples.Nine;

    private static IReadOnlyList<string> Cells(CloudRow row, int index) => [.. row.Table.Rows[index].Select(cell => cell.Text)];

    [Fact]
    public void The_cores_table_names_each_core_with_its_load_clock_and_temperature()
    {
        var row = BoxRows.Cores(BoxSamples.Snapshot());

        Assert.Equal(["Core", "Load", "Clock", "Temp"], row.Table.Columns);
        Assert.Equal(4, row.Table.Rows.Count);
        Assert.Equal(["CPU1", "26.8 %", "4.00 GHz", "41 °C"], Cells(row, 0));
        Assert.Equal(["CPU3", "28.1 %", "3.98 GHz", "40 °C"], Cells(row, 2));
        Assert.StartsWith("all 4 at 27 to 31 %", row.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_memory_table_holds_the_three_pools_and_the_answer_reads_used_of_total()
    {
        var row = BoxRows.Memory(BoxSamples.Snapshot());

        Assert.Equal("7.9 GiB of 31.3 GiB", row.Answer);
        Assert.Equal(["Memory", "Cache", "Swap"], row.Table.Rows.Select(cells => cells[0].Text));
        Assert.Equal(["Swap", "0 B", "2.0 GiB", "0 %"], Cells(row, 2));
        Assert.Equal("alarm", BoxRows.Memory(BoxSamples.Snapshot() with { Memory = new(100, 96, 1, 3) }).Tone);
        Assert.Equal("warning", BoxRows.Memory(BoxSamples.Snapshot() with { Memory = new(100, 86, 1, 13) }).Tone);
    }

    [Fact]
    public void The_network_table_names_interfaces_only_and_leaves_the_loopback_out()
    {
        var row = BoxRows.Network(BoxSamples.Snapshot());

        Assert.Equal(["Interface", "In now", "Out now", "Received", "Sent"], row.Table.Columns);
        Assert.Equal(["enp0s31f6", "tailscale0"], row.Table.Rows.Select(cells => cells[0].Text));
        Assert.Equal("17.3 KiB/s in, 708.0 KiB/s out", row.Answer);
        Assert.StartsWith("totals since boot 2.1 GiB received", row.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_disks_table_shows_each_file_system_and_the_array_on_the_one_it_holds()
    {
        var row = BoxRows.Disks(BoxSamples.Snapshot());

        Assert.Equal(["/ (md126p2)", "ext4", "2.5 TiB", "17.1 GiB", "0.7 %", "active raid0, 6 of 6 active"], Cells(row, 0));
        Assert.Equal("", Cells(row, 1)[5]);
        Assert.Equal("no array", BoxRows.Disks(BoxSamples.Snapshot() with { Disks = [BoxSamples.Snapshot().Disks[1]] }).Answer);
        Assert.Equal("warning", BoxRows.Disks(BoxSamples.Snapshot() with { Disks = [BoxSamples.Snapshot().Disks[0] with { UsedB = 2_300_000_000_000 }] }).Tone);
        Assert.Equal("alarm", BoxRows.Disks(BoxSamples.Snapshot() with { Disks = [BoxSamples.Snapshot().Disks[0] with { RaidState = "inactive raid0" }] }).Tone);
    }

    [Fact]
    public void The_temperature_table_shows_each_sensors_own_marks_and_none_for_one_without()
    {
        var row = BoxRows.Temps(BoxSamples.Snapshot());

        Assert.Equal(["coretemp Package id 0", "41 °C", "high 82 °C, critical 100 °C"], Cells(row, 0));
        Assert.Equal(["acpitz temp1", "30 °C", ""], Cells(row, 1));
        Assert.Equal("no sensor read", BoxRows.Temps(BoxSamples.Snapshot() with { Temps = [] }).Answer);
    }

    [Fact]
    public void The_services_table_says_how_long_each_running_one_has_been_up_and_a_stopped_one_is_blank()
    {
        var row = BoxRows.Services(BoxSamples.Snapshot(), Nine);

        Assert.Equal(["claude-remote-control.service (user)", "active, running", "3 d 0 h", ""], Cells(row, 0));
        Assert.Equal(["theyard-box-agent.service (user)", "active, running", "12 min", ""], Cells(row, 2));
        Assert.Equal("", Cells(row, 3)[2]);
        Assert.Equal("next Sun 06:00", Cells(row, 4)[3]);
        Assert.Equal("alarm", BoxRows.Services(BoxSamples.Snapshot() with { Services = [] }, Nine).Tone);
    }

    [Fact]
    public void The_reporting_row_counts_from_the_later_of_the_snapshot_held_and_the_minute_kept()
    {
        var started = Nine.AddHours(-2);
        var held = BoxRows.Reporting(BoxSamples.Snapshot(Nine.AddMinutes(-10)), Nine.AddMinutes(-1), started, Nine, 50);
        var kept = BoxRows.Reporting(null, Nine.AddMinutes(-2), started, Nine, 50);
        var both = BoxRows.Reporting(BoxSamples.Snapshot(Nine.AddMinutes(-1)), Nine.AddMinutes(-5), started, Nine, 50);

        Assert.Equal(("good", "yes, the newest snapshot 1 min ago"), (held.Tone, held.Answer));
        Assert.Equal(("good", "yes, the newest snapshot 2 min ago"), (kept.Tone, kept.Answer));
        Assert.Equal("good", both.Tone);
        Assert.StartsWith("50 snapshots kept in the last hour", held.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hour_the_recorder_keeps_names_the_box_its_parts_and_its_services()
    {
        var snapshots = new MemoryBoxSnapshots();
        snapshots.Accept(BoxSamples.Snapshot());
        var source = new BoxSource(snapshots, new MemoryBoxLive(), new TheYard.Api.Shared.Instance.HostStart(new BoxSamples.Clock(Nine)), new BoxSamples.Clock(Nine));

        var hour = await source.HourAsync(Nine, Nine.AddHours(-1), CancellationToken.None);

        Assert.Equal("WickedAwsome", hour.Facts["host"]);
        Assert.Equal("4", hour.Facts["cores"]);
        Assert.Equal("31.3 GiB", hour.Facts["ram"]);
        Assert.Equal("NVIDIA GeForce RTX 2080, NVIDIA GeForce RTX 2080", hour.Facts["gpus"]);
        Assert.Contains("ssh.service active", hour.Facts["services"], StringComparison.Ordinal);
        Assert.Equal("1", hour.Facts["agent"]);
        Assert.Equal(Nine.AddSeconds(-20), source.Newest?.At);
        Assert.Null(source.LiveAt);
        Assert.Equal(Nine, source.Started);
    }
}
