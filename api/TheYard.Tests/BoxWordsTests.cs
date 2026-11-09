using TheYard.Api.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The box's card in words (ADR: The box reports itself): bytes, rates, shares, temperatures and spans the way
/// System Monitor says them, the scrub that keeps every address and port off the card while leaving times and unit
/// names whole, the readings a kept minute holds, and the package sensor the temperature row leads with.
/// </summary>
public sealed class BoxWordsTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(17_300, "16.9 KiB")]
    [InlineData(33_577_168_896, "31.3 GiB")]
    [InlineData(2_800_000_000_000, "2.5 TiB")]
    public void Bytes_read_in_binary_units(double bytes, string words) => Assert.Equal(words, BoxWords.Bytes(bytes));

    [Fact]
    public void Rates_shares_temperatures_and_spans_read_as_system_monitor_says_them()
    {
        Assert.Equal("642.5 KiB/s", BoxWords.Rate(657_903));
        Assert.Equal("25.4 %", BoxWords.Percent(25.43));
        Assert.Equal("100 %", BoxWords.Percent(100));
        Assert.Equal("not read", BoxWords.Percent(null));
        Assert.Equal("41 °C", BoxWords.Degrees(41.2));
        Assert.Equal("not read", BoxWords.Degrees(null));
        Assert.Equal("3 d 4 h", BoxWords.Span(TimeSpan.FromSeconds(273_600)));
        Assert.Equal("2 h 5 min", BoxWords.Span(TimeSpan.FromMinutes(125)));
        Assert.Equal("12 min", BoxWords.Span(TimeSpan.FromMinutes(12.5)));
        Assert.Equal("40 s", BoxWords.Span(TimeSpan.FromSeconds(40)));
        Assert.Equal("0 s", BoxWords.Span(TimeSpan.FromSeconds(-3)));
    }

    [Theory]
    [InlineData("198.51.100.7", "an address")]
    [InlineData("listening on 192.0.2.10:22", "listening on an address")]
    [InlineData("ssh on port 22", "ssh on a port")]
    [InlineData("2001:db8:a1e0::1234 is the box", "an address is the box")]
    [InlineData("fe80:0:0:0:200:f8ff:fe21:67cf", "an address")]
    [InlineData("next Sun 2026-11-08 06:00:00 CST", "next Sun 2026-11-08 06:00:00 CST")]
    [InlineData("active raid0, mdadm needs sudo", "active raid0, mdadm needs sudo")]
    [InlineData("coretemp Package id 0", "coretemp Package id 0")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void The_scrub_takes_out_every_address_and_port_and_leaves_times_and_names_whole(string? words, string scrubbed) =>
        Assert.Equal(scrubbed, BoxWords.Scrub(words));

    [Fact]
    public void A_snapshot_with_nothing_in_its_lists_still_gives_every_reading_without_dividing_by_zero()
    {
        var empty = BoxSamples.Snapshot() with
        {
            Cores = [],
            Memory = new BoxMemory(0, 0, 0, 0),
            Swap = new BoxSwap(0, 0),
            Nets = [],
            Disks = [],
            Gpus = [],
            Temps = [],
            Loadavg = [],
        };

        var values = BoxSource.Values(empty);

        Assert.Equal(BoxSource.Readings.Order(), values.Keys.Order());
        Assert.Null(values["cpu"]);
        Assert.Null(values["memory"]);
        Assert.Null(values["gpu0"]);
        Assert.Null(values["disk_root"]);
        Assert.Null(values["temp_cpu"]);
        Assert.Null(values["load1"]);
        Assert.Equal(0, values["net_in_bps"]);
        Assert.Null(BoxSource.Package(empty));
    }

    [Fact]
    public void The_package_sensor_leads_and_without_one_the_hottest_marked_sensor_does()
    {
        var snapshot = BoxSamples.Snapshot();
        var unnamed = snapshot with { Temps = [new BoxTemp("acpitz temp1", 30, null, null), new BoxTemp("nvme Composite", 44, 70, 80), new BoxTemp("it8686 temp2", 38, 90, 100)] };

        Assert.Equal("coretemp Package id 0", BoxSource.Package(snapshot)?.Sensor);
        Assert.Equal("nvme Composite", BoxSource.Package(unnamed)?.Sensor);
    }

    [Fact]
    public void A_gpu_reads_amber_ten_degrees_under_its_slowdown_mark_and_red_at_it_or_at_a_full_memory()
    {
        var snapshot = BoxSamples.Snapshot();
        BoxSnapshot Gpu(double temp, long used) => snapshot with { Gpus = [snapshot.Gpus[0] with { Temp = temp, MemUsedB = used }] };

        Assert.Equal("good", BoxRows.Gpus(Gpu(60, 412_000_000)).Tone);
        Assert.Equal("warning", BoxRows.Gpus(Gpu(88, 412_000_000)).Tone);
        Assert.Equal("alarm", BoxRows.Gpus(Gpu(97, 412_000_000)).Tone);
        Assert.Equal("warning", BoxRows.Gpus(Gpu(40, 7_100_000_000)).Tone);
        Assert.Equal("alarm", BoxRows.Gpus(Gpu(40, 7_900_000_000)).Tone);
        Assert.Equal("1 x RTX 2080", BoxRows.Gpus(Gpu(40, 0)).Answer);
        Assert.Equal("no GPU read", BoxRows.Gpus(snapshot with { Gpus = [] }).Answer);
    }

    [Fact]
    public void The_worst_tone_wins_and_no_tone_is_fine()
    {
        Assert.Equal("alarm", BoxRows.Worst(["good", "alarm", "warning"]));
        Assert.Equal("warning", BoxRows.Worst(["good", "info", "warning"]));
        Assert.Equal("info", BoxRows.Worst(["info", "good"]));
        Assert.Equal("good", BoxRows.Worst([]));
    }
}
