using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// What the box may say about itself (ADR: The box reports itself): every rule's one sentence, the limits that keep a
/// bad body from growing a document, and the three handlers over the in-memory ports with no host: the rules first,
/// the port second, the ring's size, the recorder's take, and the silence measured on this process's clock.
/// </summary>
public sealed class BoxSnapshotRulesTests
{
    private static readonly DateTimeOffset Nine = BoxSamples.Nine;

    [Fact]
    public void A_whole_snapshot_and_sample_pass_and_each_broken_rule_says_its_one_sentence()
    {
        var good = BoxSamples.Snapshot();
        Assert.Null(BoxSnapshotRules.Validate(good, Nine));
        Assert.Null(BoxSnapshotRules.Validate(BoxSamples.Sample(Nine), Nine));
        Assert.Equal("host is 1 to 40 letters, digits or dashes.", BoxSnapshotRules.Validate(good with { Host = "wicked awsome" }, Nine));
        Assert.Equal("at is more than five minutes ahead of this site's clock.", BoxSnapshotRules.Validate(good with { At = Nine.AddMinutes(6) }, Nine));
        Assert.Equal("at is more than an hour ago.", BoxSnapshotRules.Validate(good with { At = Nine.AddMinutes(-61) }, Nine));
        Assert.Equal("every list and part is present, empty when there is none.", BoxSnapshotRules.Validate(good with { Gpus = null! }, Nine));
        Assert.Equal("every load is 0 to 100.", BoxSnapshotRules.Validate(good with { AgentCpuPercent = 101 }, Nine));
        Assert.Equal("every byte count and rate is zero or more.", BoxSnapshotRules.Validate(good with { Swap = new(-1, 0) }, Nine));
        Assert.Equal("every load is 0 to 100.", BoxSnapshotRules.Validate(BoxSamples.Sample(Nine) with { Cores = [120] }, Nine));
        Assert.Equal("every byte count and rate is zero or more.", BoxSnapshotRules.Validate(BoxSamples.Sample(Nine) with { MemoryUsedB = -5 }, Nine));
    }

    [Fact]
    public void A_body_past_the_list_limits_is_refused_before_it_can_grow_a_document()
    {
        var good = BoxSamples.Snapshot();
        Assert.Equal("cores holds at most 64.", BoxSnapshotRules.Validate(good with { Cores = [.. Enumerable.Range(0, 65).Select(i => new Data.BoxCore(i, 1, 1, null))] }, Nine));
        Assert.Equal("services holds at most 32.", BoxSnapshotRules.Validate(good with { Services = [.. Enumerable.Range(0, 33).Select(i => good.Services[0])] }, Nine));
        Assert.Equal("nets holds at most 32.", BoxSnapshotRules.Validate(BoxSamples.Sample(Nine) with { Nets = [.. Enumerable.Range(0, 33).Select(i => new Data.BoxNetRate("x", 0, 0))] }, Nine));
    }

    [Fact]
    public async Task The_keep_handler_checks_the_rules_then_holds_the_snapshot_for_the_card_and_the_recorder()
    {
        var snapshots = new MemoryBoxSnapshots();
        var keep = new KeepBoxSnapshotHandler(snapshots, new BoxSamples.Clock(Nine));

        Assert.Equal("host is 1 to 40 letters, digits or dashes.", (await keep.HandleAsync(new(BoxSamples.Snapshot() with { Host = "" }), CancellationToken.None)).Wrong);
        Assert.Null(snapshots.Newest);
        Assert.Null((await keep.HandleAsync(new(BoxSamples.Snapshot(Nine.AddMinutes(-2))), CancellationToken.None)).Wrong);
        Assert.Null((await keep.HandleAsync(new(BoxSamples.Snapshot(Nine.AddMinutes(-1))), CancellationToken.None)).Wrong);

        Assert.Equal(Nine.AddMinutes(-1), snapshots.Newest?.At);
        Assert.Equal([Nine.AddMinutes(-2), Nine.AddMinutes(-1)], snapshots.TakeUnrecorded().Select(snapshot => snapshot.At));
        Assert.Empty(snapshots.TakeUnrecorded());
        Assert.NotNull(snapshots.Newest);
    }

    [Fact]
    public async Task The_ring_holds_sixty_samples_and_the_live_read_says_how_long_the_box_has_been_silent()
    {
        var live = new MemoryBoxLive();
        var clock = new BoxSamples.Clock(Nine);
        var push = new PushBoxSampleHandler(live, clock);
        var read = new ReadBoxLiveHandler(live, clock);

        var before = await read.HandleAsync(new ReadBoxLiveQuery(), CancellationToken.None);
        Assert.Null(before.SilentSeconds);
        Assert.Empty(before.Samples);

        for (int i = 0; i < 70; i++)
        {
            Assert.Null((await push.HandleAsync(new(BoxSamples.Sample(Nine.AddSeconds(-140 + (2 * i)))), CancellationToken.None)).Wrong);
        }
        Assert.Equal("every load is 0 to 100.", (await push.HandleAsync(new(BoxSamples.Sample(Nine) with { Gpus = [new(0, -1)] }), CancellationToken.None)).Wrong);
        clock.Now = Nine.AddSeconds(4);
        var after = await read.HandleAsync(new ReadBoxLiveQuery(), CancellationToken.None);

        Assert.Equal(MemoryBoxLive.Size, live.Last(TimeSpan.FromHours(1)).Count);
        Assert.Equal(6, after.SilentSeconds);
        Assert.Equal(2, after.CadenceSeconds);
        Assert.Equal(31, after.Samples.Count);
        Assert.True(after.Samples[0].At < after.Samples[^1].At);
    }
}
