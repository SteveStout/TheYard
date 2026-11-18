using System.Globalization;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The share card's dial and its minute sparks (ADR-102, the addendum on the share's sparks): the dial's sixty-minute
/// sum is the sparks' own minutes, turned back from a share of the alert's pace into counts, so the two cannot drift.
/// </summary>
public sealed partial class StorageCardTests
{
    /// <summary>
    /// On 18 November the dial read 30,407 while the three sparks' values summed to about 9,050 and the review called the
    /// dial three times its own sparks. Azure's Transactions for the same sixty minutes read 33,113 two minutes later, so
    /// the dial was right: the sparks are each minute as a per cent of the alert's 333 a minute, not counts. After 1.0.5.89
    /// the dial read 40,795 and three filtered sparks 37,823: calls no filter named, Close among them (291 of 6,616 in the
    /// next hour), were in the dial and in no spark. The third spark is now the minute's total less its listings and
    /// writes, so this pins the dial to the sparks with such calls in every minute, a burst included.
    /// </summary>
    [Fact]
    public void The_share_dial_is_its_own_sparks_summed_back_into_counts_and_the_sparks_say_their_unit()
    {
        var minutes = Enumerable.Range(0, 60).Select(i =>
        {
            double listings = i >= 58 ? 2_400 : 57 + i % 7;
            double reads = i % 10 == 0 ? 500 : 15 + i % 4;
            double writes = i % 3;
            double close = 5 + i % 2;
            return new ResourceMinute(Noon.AddMinutes(i - 60), new Dictionary<string, double?>
            {
                ["transactions"] = listings + reads + writes + close,
                ["listings"] = listings,
                ["writes"] = writes,
                ["used_gb"] = 29,
                ["files"] = 9_668,
            }, "test");
        }).ToList();
        var inputs = Inputs(1, ShareFacts()) with { LastHour = minutes };

        var card = ShareCard.View(inputs);

        double pace = 20_000 / 60.0;
        double fromSparks = card.Sparks.Sum(spark => spark.Values.Sum(v => v ?? 0)) * pace / 100;
        double dial = double.Parse(card.Dials[0].Big, NumberStyles.Number, CultureInfo.InvariantCulture);
        double counted = minutes.Sum(m => m.Values["transactions"]!.Value);
        Assert.Equal(counted, dial, 0);
        // Each spark point is a share rounded to 0.01 %, a third of a transaction at this pace, so 180 points may differ by a few.
        Assert.InRange(fromSparks, dial - 3, dial + 3);
        Assert.Equal(["Listings", "Reads and the rest", "Writes"], card.Sparks.Select(s => s.Name.Split(',')[0]));
        Assert.All(card.Sparks, spark => Assert.EndsWith("60 min · % of the alert's 333 a minute", spark.Name, StringComparison.Ordinal));
        Assert.Equal(minutes.Select(m => m.Values["transactions"] - m.Values["listings"] - m.Values["writes"]), ShareCard.Rest(inputs));
        Assert.Null(ShareCard.Rest(inputs with { LastHour = [new ResourceMinute(Noon, new Dictionary<string, double?> { ["listings"] = 3 }, "test")] })[0]);
        Assert.DoesNotContain(ShareCard.Metrics, metric => metric.Reading == "reads");
    }
}
