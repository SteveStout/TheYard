using TheYard.Application.Features.Visitors;
using TheYard.Infrastructure.Cosmos.Features.Visitors;

namespace TheYard.Tests;

/// <summary>
/// The three document adapters that keep what the operator reads back: the kept log, the site's
/// activity counters and the visitors counted once. Against the real account, under the same trait as
/// <c>CosmosStoreTests</c>, so CI filters them out and the ship gate runs them as the signed-in
/// principal.
///
/// <para>Each test asks the store whether it can keep anything BEFORE it asserts that something was
/// kept, which is the guard <c>ClaudeUsageTests</c> and <c>NightlyCardTests</c> already use and is not
/// optional here: the test database the gate points this suite at is seeded with four containers,
/// vehicles, photos, bids and users, and the activity and logs containers are not among them. On that
/// database the only true thing to assert is the refusal, and the refusal is worth asserting, because
/// it is what every container that has not had <c>infra/cosmos/</c> applied to it answers and the
/// Admin tab shows those words to a person. Where the containers ARE there, the same test goes on to
/// write its own documents under an id nobody else uses and takes them away again, rather than
/// reading what another test left, which is the defect <c>AuthBidTests</c> has cost this lane twice.</para>
/// </summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public sealed class CosmosHistoryStoreTests
{
    /// <summary>A day of its own, far enough back that no live window reads it and no other test writes it.</summary>
    private static DateTimeOffset AnOldHour(int hoursBack) =>
        new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero).AddHours(-hoursBack);

    // #region logs
    /// <summary>
    /// The kept log. A container that is not there is refused by name, with the file that would
    /// create it, and then keeps nothing and reads back empty rather than throwing at the collector
    /// writing off the request path. A container that is there takes a batch, reads it back under its
    /// own kind and counts it, and the whole exchange is charged for, because the card reports what
    /// the feature costs and a zero charge would mean nothing was asked.
    /// </summary>
    [Fact]
    public async Task The_kept_log_names_the_container_it_needs_or_keeps_a_batch_and_reads_it_back()
    {
        var (store, _) = CosmosLive.Open();
        var logs = new CosmosLogStore(store);

        var said = await logs.AvailabilityAsync(CancellationToken.None);
        if (!said.Available)
        {
            Assert.Equal("the logs container is not there; apply infra/cosmos/logs.json", said.Reason);
            await logs.AppendAsync([Event(AnOldHour(1), "/kept-nothing")], CancellationToken.None);
            Assert.Empty(await logs.QueryAsync(new LogQuery(AnOldHour(2), null, null, null, 50), CancellationToken.None));
            Assert.Empty(await logs.CountAsync(AnOldHour(2), CancellationToken.None));
            return;
        }

        Assert.Contains("kept in Azure Cosmos DB", said.Reason, StringComparison.Ordinal);
        string path = $"/canary/{Guid.NewGuid():N}";
        var at = AnOldHour(1);

        await logs.AppendAsync([Event(at, path), Event(at.AddSeconds(1), path)], CancellationToken.None);

        var read = await logs.QueryAsync(new LogQuery(at.AddHours(-1), LogEvent.RequestKind, 200, path, 50), CancellationToken.None);
        Assert.Equal(2, read.Count);
        Assert.All(read, kept => Assert.Equal((LogEvent.RequestKind, path, 200), (kept.Kind, kept.Path, kept.Status)));
        // Newest first, which is the order every card draws.
        Assert.Equal(at.AddSeconds(1), read[0].At);
        Assert.Contains(await logs.CountAsync(at.AddHours(-1), CancellationToken.None), count => count.Kind == LogEvent.RequestKind);
        Assert.True(logs.Cost.Charge > 0, "a batch that was written was charged for");
        Assert.Equal(0, logs.Cost.Failures);
    }

    /// <summary>One request event on a path nobody else uses, cleaned the way the collector cleans one.</summary>
    private static LogEvent Event(DateTimeOffset at, string path) =>
        new(at, LogEvent.RequestKind, "cosmos", "Information", "TheYard", "GET", path, 200, 3, "0123456789abcdef0123456789abcdef", "203.0.113.x", "", "", Guid.NewGuid().ToString("N"), null, 3_600);
    // #endregion logs

    // #region activity
    /// <summary>
    /// The activity counters. The refusal names the container and the file, and then a batch is
    /// dropped and both reads come back empty. Where the container is there, two batches in one hour
    /// fold into one hour row moved by its delta rather than a row per request, which is the whole
    /// reason the adapter folds before it writes, and the request units it spent are reported for the
    /// card.
    /// </summary>
    [Fact]
    public async Task The_activity_counters_name_the_container_they_need_or_fold_two_batches_into_one_hour()
    {
        var (store, _) = CosmosLive.Open();
        var activity = new CosmosActivityStore(store);

        var said = await activity.AvailabilityAsync(CancellationToken.None);
        if (!said.Available)
        {
            Assert.Equal("the activity container is not there; apply infra/cosmos/activity.json", said.Reason);
            await activity.RecordAsync([Hit(AnOldHour(3), "kept-nothing", "/")], CancellationToken.None);
            Assert.Empty(await activity.HoursAsync(AnOldHour(4), CancellationToken.None));
            Assert.Empty(await activity.VisitorsAsync(AnOldHour(4), CancellationToken.None));
            return;
        }

        // The retention line is the reason and the retention both, because a card shows one sentence.
        Assert.Equal(said.Reason, said.Retention);
        var hour = AnOldHour(3);
        string visitor = Guid.NewGuid().ToString("N");

        await activity.RecordAsync([Hit(hour.AddMinutes(1), visitor, "/"), Hit(hour.AddMinutes(2), visitor, "/")], CancellationToken.None);
        await activity.RecordAsync([Hit(hour.AddMinutes(3), visitor, "/api/vehicles", bot: true)], CancellationToken.None);

        var mine = Assert.Single(await activity.VisitorsAsync(hour.AddHours(-1), CancellationToken.None), row => row.Visitor == visitor);
        Assert.Equal((3, 1, "203.0.113.x"), (mine.Requests, mine.Bots, mine.Network));
        Assert.Equal(hour.AddMinutes(1), mine.FirstSeen);
        Assert.Equal(hour.AddMinutes(3), mine.LastSeen);
        Assert.Equal(2, mine.Paths["/"]);

        var hours = await activity.HoursAsync(hour.AddHours(-1), CancellationToken.None);
        var folded = Assert.Single(hours, row => row.Hour == hour && row.Store == "cosmos");
        Assert.True(folded.Requests >= 3, $"the hour holds this test's three requests and whatever else that hour kept; it read {folded.Requests}");
        Assert.True(activity.Cost.RequestUnits > 0, "the fold that was written was charged for");
    }

    /// <summary>One hit, already reduced to a keyed hash and a cleaned path, the way the recorder offers one.</summary>
    private static ActivityHit Hit(DateTimeOffset at, string visitor, string path, bool bot = false) =>
        new(at, visitor, "203.0.113.x", path, "cosmos", bot);
    // #endregion activity

    // #region visitors
    /// <summary>
    /// The visitors counted once (ADR-116). This adapter says where it reads from in words the card
    /// shows, and it answers whether it is there by reading the container rather than by being told,
    /// so a container applied after the process started begins being written to without a restart.
    /// Where it is there, a sighting is kept under a hash of its own and the tally the card reads
    /// counts it; the document is taken away again, because the tally is a count of people and a
    /// canary left behind would be a person who never visited.
    /// </summary>
    [Fact]
    public async Task The_visitors_store_says_where_it_reads_from_and_counts_a_sighting_it_kept()
    {
        var (store, _) = CosmosLive.Open();
        var visitors = new CosmosVisitorStore(store, TimeProvider.System);

        Assert.Equal("Cosmos DB, the activity container's visitors", visitors.Source);
        Assert.Equal("visitors", CosmosVisitorStore.Partition);

        if (!await visitors.AvailableAsync(CancellationToken.None))
        {
            // The same container the counters need, so the same refusal: nothing is kept, and the
            // tally the card reads is empty rather than wrong.
            await visitors.KeepAsync([Sighting(Guid.NewGuid().ToString("N"))], CancellationToken.None);
            Assert.Equal(0, (await visitors.TallyAsync(Day, CancellationToken.None)).People);
            return;
        }

        // The tally is held for a minute per store (TallyAge), so every reading below is taken
        // through a store of its own; one instance asked twice inside the minute answers from what
        // it already read, which is asserted at the end rather than worked around silently.
        string who = Guid.NewGuid().ToString("N");
        var before = await visitors.TallyAsync(Day, CancellationToken.None);

        await visitors.KeepAsync([Sighting(who)], CancellationToken.None);

        var after = await Tally(store);
        Assert.Equal(before.People + 1, after.People);
        Assert.Equal(before.FirstSeenByDay.GetValueOrDefault(Day) + 1, after.FirstSeenByDay.GetValueOrDefault(Day));

        // The same sighting again is the same person and not a second one, because the document's id
        // is the hash and a day it was already seen on moves nothing.
        await visitors.KeepAsync([Sighting(who)], CancellationToken.None);
        Assert.Equal(after.People, (await Tally(store)).People);

        // The held tally: this store read the count before the sighting was kept and still answers
        // with it, which is what keeps a card from costing a partition read a request.
        Assert.Equal(before.People, (await visitors.TallyAsync(Day, CancellationToken.None)).People);

        await store.DeleteAsync<VisitorDocument>(
            store.ContainerNamed(Containers.Activity),
            who,
            CosmosVisitorStore.Partition,
            "pinned to the visitors");
        Assert.Equal(before.People, (await Tally(store)).People);
    }

    /// <summary>The tally through a store of its own, so the minute-long hold on the last one is not read back.</summary>
    private static Task<VisitorTally> Tally(CosmosStore store) =>
        new CosmosVisitorStore(store, TimeProvider.System).TallyAsync(Day, CancellationToken.None);

    /// <summary>The UTC day the sighting is kept under, far enough back that no live window reads it.</summary>
    private const string Day = "2026-01-02";

    /// <summary>One sighting of a person, on a day of its own, under a hash nothing else uses.</summary>
    private static VisitorSighting Sighting(string who) => new(who, Day, VisitorKind.People);
    // #endregion visitors
}
