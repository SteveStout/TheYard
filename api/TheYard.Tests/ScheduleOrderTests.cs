using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The schedule order is a faster way to the page the full sort gives, and
/// nothing else (ADR: The search index, the addendum on the schedule order):
/// at every instant of a week it reads the catalogue in exactly the order
/// VehicleOrdering's ending-soonest sort puts it, ties included.
/// </summary>
public class ScheduleOrderTests
{
    private static readonly IReadOnlyList<Vehicle> Catalogue =
        Enumerable.Range(0, 1_000).Select(i => TestData.Vehicle(id: $"probe-{i}", make: i % 3 == 0 ? "Ford" : "Kia")).ToList();

    /// <summary>Every three hours from two days before the anchor to a week after it, and the anchor itself.</summary>
    public static TheoryData<long> Instants()
    {
        var data = new TheoryData<long>();
        for (long hour = -48; hour <= 168; hour += 3)
        {
            data.Add(hour * 60 * 60 * 1000);
        }

        return data;
    }

    /// <summary>Every position the order hands out at <paramref name="nowMs"/>, in order.</summary>
    private static List<int> InOrder(ScheduleOrder order, long nowMs)
    {
        var read = new List<int>();
        order.Visit(nowMs, index =>
        {
            read.Add(index);
            return true;
        });
        return read;
    }

    [Fact]
    public void The_walk_stops_when_the_caller_has_its_page()
    {
        var order = new ScheduleOrder(Catalogue, Anchor);
        int read = 0;

        order.Visit(Anchor, _ => ++read < 10);

        Assert.Equal(10, read);
    }

    private static readonly long Anchor =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4))).AnchorMs;

    [Theory]
    [MemberData(nameof(Instants))]
    public void The_order_is_the_full_sort_at_every_instant(long sinceAnchorMs)
    {
        var clock = new AuctionClock(Anchor + sinceAnchorMs, Anchor);
        var order = new ScheduleOrder(Catalogue, Anchor);

        var expected = VehicleOrdering.Sort(Catalogue, VehicleSort.EndingSoonest, clock).Select(v => v.Id).ToList();
        var actual = InOrder(order, clock.NowMs).Select(index => Catalogue[index].Id).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_tie_keeps_the_catalogue_order_in_every_band()
    {
        // The same id twice has the same window, the only way two vehicles tie exactly.
        var twins = new[]
        {
            TestData.Vehicle(id: "twin"), TestData.Vehicle(id: "other"), TestData.Vehicle(id: "twin"),
        };
        var order = new ScheduleOrder(twins, Anchor);
        var window = AuctionSchedule.Window("twin", Anchor);

        foreach (long now in new[] { window.StartsAtMs - 1, window.StartsAtMs, window.EndsAtMs })
        {
            var positions = InOrder(order, now).Where(index => twins[index].Id == "twin").ToList();
            Assert.Equal([0, 2], positions);
        }
    }

    [Fact]
    public void Every_vehicle_is_read_once()
    {
        var order = new ScheduleOrder(Catalogue, Anchor);

        var read = InOrder(order, Anchor + (5 * 60 * 60 * 1000));

        Assert.Equal(Catalogue.Count, read.Count);
        Assert.Equal(Catalogue.Count, read.Distinct().Count());
        Assert.Equal(Catalogue.Count, order.Count);
    }
}
