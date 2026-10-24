using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// Where the nightly card keeps what it reads, and the snapshot count it reads with Reader alone
/// (ADR: The Nightly Function on the Admin tab, the addendum of 1.0.4.25): the minutes, hours and
/// runs share the plan container under kinds of their own, because the shared database cannot hold
/// a twenty-sixth container, and the share snapshots are counted from Resource Manager's list of the
/// storage account's shares, which needs no data role and no account key.
/// </summary>
public sealed partial class NightlyCardTests
{
    // #region nightly-kept-tests
    [Fact]
    public void The_nightly_card_keeps_its_minutes_in_the_plan_container_under_kinds_of_its_own()
    {
        var azure = new AzureMetrics("client-1", configured: false);
        var log = new UnkeptRunLog();
        var source = new NightlySource(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST-NIGHTLY", TimeProvider.System), Names,
            new NightlyRunReader(new GitHubRuns("owner/repo", "nightly.yml"), log, new NightlyRunCache(), new FixedClock(Now)), log, FunctionPath);
        var site = new AzureResources("RG-TEST", "app-test", "sqldb-test", "cosmos-test", "crtest", "log-test", "acs-test", "edge-test", new Uri("https://yard.example"), new DateOnly(2026, 10, 30));
        var cards = ResourceCatalog.Cards(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST", TimeProvider.System), new NetlifyEdge(null, "edge-test", new Uri("https://yard.example")), (3, "m1"), site, nightly: source);

        var nightly = cards.Single(card => card.Key == "nightly");
        Assert.Equal((Containers.Plan, Containers.NightlyPrefix), (nightly.Container, nightly.KindPrefix));
        // Every card's keep is a container the definitions hold, and no two cards read the same kinds;
        // the plan card, which is not in the catalogue, keeps the plan container's unprefixed kinds.
        Assert.All(cards, card => Assert.True(Containers.PartitionKeyPaths.ContainsKey(card.Container), $"the {card.Key} card keeps its minutes in {card.Container}, which infra/cosmos does not define"));
        Assert.Equal(cards.Count, cards.Select(card => (card.Container, card.KindPrefix)).Distinct().Count());
        Assert.DoesNotContain(cards, card => card.Container == Containers.Plan && card.KindPrefix.Length == 0);
    }

    [Fact]
    public void The_share_snapshots_are_counted_from_resource_manager_s_list_of_shares()
    {
        using var shares = JsonDocument.Parse("""
            {"value":[
              {"name":"jobhunt","properties":{"shareQuota":100}},
              {"name":"jobhunt","properties":{"shareQuota":100,"snapshotTime":"2026-10-20T09:00:04.0000000Z"}},
              {"name":"jobhunt","properties":{"shareQuota":100,"snapshotTime":"2026-10-21T09:00:03.0000000Z"}},
              {"name":"jobhunt","properties":{"shareQuota":100,"snapshotTime":"2026-10-22T09:00:05.0000000Z"}},
              {"name":"other","properties":{"shareQuota":5,"snapshotTime":"2026-10-22T09:00:05.0000000Z"}}
            ]}
            """);
        Assert.Equal(3, NightlySource.SnapshotCount(shares.RootElement));
        using var none = JsonDocument.Parse("{}");
        Assert.Equal(0, NightlySource.SnapshotCount(none.RootElement));
        Assert.Equal("3 of the 14 the Function keeps", NightlyCard.SnapshotWords("3", "not read yet"));
        Assert.Equal("not read: Resource Manager answered 403 for the shares of sttest", NightlyCard.SnapshotWords("not read: Resource Manager answered 403 for the shares of sttest", "not read yet"));
        Assert.Equal("not read yet", NightlyCard.SnapshotWords(null, "not read yet"));
    }
    [Fact]
    public void Until_the_kept_minutes_hold_a_slot_the_hour_s_own_day_of_slots_fills_the_dial()
    {
        // 22 October, live after 1.0.4.25 rolled: the keep had minutes from 10:5x UTC, no slot had
        // come due inside them, and the dial read "no slot kept yet" beside a Last fired the hour had read.
        var fired = new DateTimeOffset(2026, 10, 22, 9, 0, 40, TimeSpan.Zero);
        var firings = new List<TheYard.Data.NightlyFiring>
        {
            new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero), "reply", TheYard.Domain.Features.Admin.NightlySlots.OnTime, fired),
            new(new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.Zero), "records", TheYard.Domain.Features.Admin.NightlySlots.Late, fired.AddHours(-3.9)),
            new(new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero), "records", TheYard.Domain.Features.Admin.NightlySlots.Missed, null),
        };
        Assert.Equal("3 1 1 1", NightlySource.SlotsWords(firings));
        Assert.Equal(new TheYard.Domain.Features.Admin.SlotTally(3, 1, 1, 1), NightlyCard.HourTally("3 1 1 1"));
        Assert.Null(NightlyCard.HourTally("0 0 0 0"));
        Assert.Null(NightlyCard.HourTally(null));

        var hour = new ResourceHour(Now.AddMinutes(-10), "", null, new Dictionary<string, string?> { [NightlySource.SlotsFact] = "3 1 1 1" }, []);
        var fresh = new ResourceCardInputs("24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), [], [], [], [hour], null, "the test site");
        Assert.Equal(new TheYard.Domain.Features.Admin.SlotTally(3, 1, 1, 1), NightlyCard.Tally(fresh));
        Assert.Equal("1 of 3 slots", NightlyCard.View(fresh, Names, FunctionPath, []).Dials[0].Absolute);
    }
    // #endregion nightly-kept-tests
}
