using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Application.Shared.Live;

namespace TheYard.Tests;

/// <summary>
/// The live pattern made general (ADR: Live dials where the source allows): the one ring type and its bound, the lease
/// that runs a source only while somebody looks, the read that leases and answers, the sampler that reads each leased
/// source at its own grain and survives one that throws, the telemetry filter over every live route and the site's own
/// self-reads, and the route for every card, which answers 200 with available false where there is no source.
/// </summary>
public sealed class LiveTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    /// <summary>A source that counts its reads, and throws when told to.</summary>
    private sealed class Counting(string key, int grain, bool throws = false, string? absent = null) : ILiveSource
    {
        public int Reads { get; private set; }

        public string Key => key;

        public int GrainSeconds => grain;

        public string Source => "a test's own count";

        public string? Absent => absent;

        public Task<LiveSample?> SampleAsync(DateTimeOffset now, CancellationToken cancellation)
        {
            Reads++;
            return throws ? throw new InvalidOperationException("the source broke") : Task.FromResult<LiveSample?>(new LiveSample(now, new Dictionary<string, double?> { ["n"] = Reads }));
        }
    }

    [Fact]
    public void The_ring_holds_its_size_drops_the_oldest_and_reads_back_by_age()
    {
        var ring = new LiveRing<LiveSample>(3, sample => sample.At);
        Assert.Null(ring.NewestAt);
        Assert.Empty(ring.Last(TimeSpan.FromHours(1)));

        for (int i = 0; i < 5; i++)
        {
            ring.Push(new LiveSample(Nine.AddSeconds(i * 15), new Dictionary<string, double?>()));
        }

        Assert.Equal(3, ring.Size);
        Assert.Equal(Nine.AddSeconds(60), ring.NewestAt);
        Assert.Equal([Nine.AddSeconds(30), Nine.AddSeconds(45), Nine.AddSeconds(60)], ring.Last(TimeSpan.FromHours(1)).Select(sample => sample.At));
        Assert.Equal([Nine.AddSeconds(45), Nine.AddSeconds(60)], ring.Last(TimeSpan.FromSeconds(15)).Select(sample => sample.At));
        ring.Clear();
        Assert.Null(ring.NewestAt);
    }

    [Fact]
    public void A_read_leases_its_card_for_a_minute_and_no_longer()
    {
        var lease = new LiveLease();
        Assert.False(lease.Leased("sqldb", Nine));

        lease.Touch("sqldb", Nine);

        Assert.True(lease.Leased("sqldb", Nine.AddSeconds(60)));
        Assert.False(lease.Leased("sqldb", Nine.AddSeconds(61)));
        Assert.False(lease.Leased("cosmosdb", Nine));
    }

    [Fact]
    public async Task The_read_leases_a_source_answers_its_ring_and_says_why_when_there_is_none()
    {
        var rings = new LiveRings();
        var lease = new LiveLease();
        ILiveSource[] sources = [new Counting("sqldb", 15), new Counting("cosmosdb", 5, absent: "this site keeps no document store")];
        var handler = new ReadLiveHandler(sources, rings, lease, new BoxSamples.Clock(Nine));
        rings.For("sqldb").Push(new LiveSample(Nine.AddSeconds(-20), new Dictionary<string, double?> { ["dtu"] = 12 }));
        rings.For("sqldb").Push(new LiveSample(Nine.AddMinutes(-11), new Dictionary<string, double?> { ["dtu"] = 99 }));

        var sql = await handler.HandleAsync(new ReadLiveQuery("sqldb"), CancellationToken.None);
        var cosmos = await handler.HandleAsync(new ReadLiveQuery("cosmosdb"), CancellationToken.None);
        var edge = await handler.HandleAsync(new ReadLiveQuery("edge"), CancellationToken.None);

        Assert.True(sql.Available);
        Assert.Equal((15, "a test's own count"), (sql.GrainSeconds, sql.Source));
        Assert.Equal([12.0], sql.Samples.Select(sample => sample.Values["dtu"]));
        Assert.True(lease.Leased("sqldb", Nine));
        Assert.Equal((false, "this site keeps no document store"), (cosmos.Available, cosmos.Note));
        Assert.False(lease.Leased("cosmosdb", Nine));
        Assert.Equal((false, ReadLiveHandler.NoSource), (edge.Available, edge.Note));
    }

    [Fact]
    public async Task The_sampler_reads_only_leased_sources_at_their_grain_and_survives_one_that_throws()
    {
        var clock = new BoxSamples.Clock(Nine);
        var rings = new LiveRings();
        var lease = new LiveLease();
        var sql = new Counting("sqldb", 15);
        var broken = new Counting("plan", 2, throws: true);
        var idle = new Counting("cosmosdb", 5);
        var sampler = new LiveSampler([sql, broken, idle], rings, lease, clock, NullLogger<LiveSampler>.Instance);

        Assert.Equal(0, await sampler.TickOnceAsync(CancellationToken.None));
        lease.Touch("sqldb", Nine);
        lease.Touch("plan", Nine);

        Assert.Equal(1, await sampler.TickOnceAsync(CancellationToken.None));
        clock.Now = Nine.AddSeconds(10);
        Assert.Equal(0, await sampler.TickOnceAsync(CancellationToken.None));
        clock.Now = Nine.AddSeconds(16);
        Assert.Equal(1, await sampler.TickOnceAsync(CancellationToken.None));

        Assert.Equal(2, sql.Reads);
        Assert.Equal(3, broken.Reads);
        Assert.Equal(0, idle.Reads);
        Assert.Equal(2, rings.For("sqldb").Last(TimeSpan.FromHours(1)).Count);
        Assert.Null(rings.For("plan").NewestAt);
        clock.Now = Nine.AddSeconds(90);
        Assert.Equal(0, await sampler.TickOnceAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("/api/admin/cloud/sqldb/live", null, false)]
    [InlineData("/api/admin/cloud/box/live", null, false)]
    [InlineData("/api/admin/plan/live", null, false)]
    [InlineData("/api/admin/cloud/sqldb", null, true)]
    [InlineData("/api/admin/cloud/nightly/runs/live", null, true)]
    [InlineData("/api/vehicles", "Mozilla/5.0", true)]
    [InlineData("/api/vehicles", "Mozilla/5.0 TheYard-SelfRead/1", false)]
    [InlineData("/healthz", "theyard-selfread", false)]
    public void The_telemetry_leaves_out_every_live_route_and_the_sites_own_reads(string path, string? agent, bool traced) =>
        Assert.Equal(traced, TelemetryRegistration.Traced(new PathString(path), agent));

    [Fact]
    public void Each_card_has_one_ring_held_for_the_life_of_the_process()
    {
        var rings = new LiveRings();

        Assert.Same(rings.For("sqldb"), rings.For("sqldb"));
        Assert.NotSame(rings.For("sqldb"), rings.For("plan"));
        Assert.Equal(LiveRings.Size, rings.For("cosmosdb").Size);
    }

    [Fact]
    public async Task The_sampler_runs_with_the_host_and_stops_when_it_stops()
    {
        var lease = new LiveLease();
        var source = new Counting("sqldb", 1);
        lease.Touch("sqldb", DateTimeOffset.UtcNow.AddSeconds(30));
        using var sampler = new LiveSampler([source], new LiveRings(), lease, TimeProvider.System, NullLogger<LiveSampler>.Instance);

        await sampler.StartAsync(CancellationToken.None);
        for (int waited = 0; source.Reads == 0 && waited < 50; waited++)
        {
            await Task.Delay(100);
        }
        await sampler.StopAsync(CancellationToken.None);
        int stopped = source.Reads;
        await Task.Delay(1500);

        Assert.True(stopped >= 1, "the sampler read the leased source while the host ran");
        Assert.Equal(stopped, source.Reads);
    }

    [Theory]
    [InlineData("/api/admin/cloud/sqldb/live", true)]
    [InlineData("/api/admin/plan/live", true)]
    [InlineData("/api/admin/cloud/live", false)]
    [InlineData("/api/admin/cloud/sqldb/live/extra", false)]
    [InlineData("/api/admin/cloud/sqldb/lively", false)]
    [InlineData("/api/vehicles/live", false)]
    public void A_live_route_is_a_cards_live_path_or_the_plans_and_nothing_else(string path, bool live) =>
        Assert.Equal(live, TelemetryRegistration.IsLive(path));
}

/// <summary>The live route as it is mapped: every card answers 200, and the box keeps its own route.</summary>
public sealed class LiveRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Every_card_has_a_live_route_that_answers_200_and_the_box_keeps_its_own()
    {
        var client = factory.CreateClient();
        using var sql = JsonDocument.Parse(await client.GetStringAsync("/api/admin/cloud/sqldb/live"));
        using var box = JsonDocument.Parse(await client.GetStringAsync("/api/admin/cloud/box/live"));

        Assert.False(sql.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(ReadLiveHandler.NoSource, sql.RootElement.GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Array, sql.RootElement.GetProperty("samples").ValueKind);
        Assert.Equal(2, box.RootElement.GetProperty("cadence_seconds").GetInt32());
    }
}
