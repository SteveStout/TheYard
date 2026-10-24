using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The job-hunt share card and the blob service card (ADR: One frame for every resource, the addendum on the
/// storage cards): what each row says with nothing read, one read and many; the grade under half the alert's
/// line, at half and at the line; the blob card red when anything is public; every row and line sourced; and
/// the hourly series and the blob listing read back from recorded answers.
/// </summary>
public sealed class StorageCardTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(double perMinute, IReadOnlyDictionary<string, string?> facts, int minutes = 60)
    {
        var values = new Dictionary<string, double?> { ["transactions"] = perMinute, ["listings"] = perMinute / 3, ["reads"] = perMinute / 3, ["writes"] = perMinute / 3, ["used_gb"] = 29, ["files"] = 9_668 };
        var lastHour = Enumerable.Range(0, minutes).Select(i => new ResourceMinute(Noon.AddMinutes(i - minutes), values, "test")).ToList();
        var bucket = new ResourceBucket(Noon.AddMinutes(-5), 5, values, values, values, "test");
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Noon, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket],
            [new ResourceHour(Noon.AddHours(-1), "/subscriptions/s/resourceGroups/RG-JOBHUNT-SS/providers/Microsoft.Storage/storageAccounts/stjobhuntss/fileServices/default/shares/jobhunt", "tenant", facts, [])], 0.31);
    }

    private static Dictionary<string, string?> ShareFacts() => new(StringComparer.Ordinal)
    {
        ["alert_threshold"] = "20000",
        ["alert_enabled"] = "true",
        ["quota_gb"] = "100",
        ["tier"] = "Transaction optimized",
        ["sku"] = "Standard_LRS",
        ["auth_hour"] = "AccountKey=6048;SAS=459",
        ["api_rows"] = "ListFiles\t2,390\t57,000\nCreateDirectory\t40\t52,000",
        ["cost_today"] = "0.31",
        ["cost_yesterday"] = "24.99",
        ["cost_month"] = "44.38",
        ["cost_rows"] = "2026-10-23\t$24.99\t\n2026-10-24\t$0.31\t",
        ["listings_hourly"] = Series.Encode(Enumerable.Range(0, 168).ToDictionary(i => Noon.AddHours(i - 168), _ => 2_400d)),
        ["tx_hourly"] = Series.Encode(Enumerable.Range(0, 720).ToDictionary(i => Noon.AddHours(i - 720), i => i < 690 ? 6_500d : 1_200_000d)),
    };

    [Theory]
    [InlineData(100, "good")]
    [InlineData(10_000.0 / 60, "warning")]
    [InlineData(20_000.0 / 60, "alarm")]
    public void The_share_is_fine_under_half_the_alert_s_line_amber_from_half_and_red_at_the_line(double perMinute, string tone)
    {
        var card = ShareCard.View(Inputs(perMinute, ShareFacts()));

        Assert.Equal(tone, card.Dials[0].Tone);
        Assert.Contains("of 20,000, the alert's line", card.Dials[0].Absolute, StringComparison.Ordinal);
        Assert.Equal("The job-hunt share", card.Title);
    }

    [Fact]
    public void The_share_s_four_questions_answer_from_what_was_read_and_say_so_when_nothing_was()
    {
        var card = ShareCard.View(Inputs(100, ShareFacts()));

        Assert.Equal(["reading", "cost", "walking", "on"], card.Rows.Select(r => r.Key));
        Assert.Equal("6,000", card.Rows[0].Answer);
        Assert.Equal(2, card.Rows[0].Table.Rows.Count);
        Assert.Contains("turn on the share's diagnostic log", card.Rows[0].Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("$0.31", card.Rows[1].Answer);
        Assert.Equal("No", card.Rows[2].Answer);
        Assert.Equal("9,668 files", card.Rows[3].Answer);
        Assert.Contains("the job hunt's Function, by the account key: 6,048", card.Readout.Lines.Single(l => l.Label == "Who reads it on a timer").Value, StringComparison.Ordinal);

        var empty = ShareCard.View(Inputs(100, new Dictionary<string, string?>(), minutes: 0));
        Assert.Equal("not on the bill yet", empty.Rows[1].Answer);
        Assert.Equal("not read yet", empty.Rows[2].Answer);
        Assert.Equal("the alert's line is not read", empty.Dials[0].Absolute);
    }

    [Fact]
    public void A_walker_is_four_times_the_quiet_hour_and_over_a_thousand_listings()
    {
        var facts = ShareFacts();
        var walked = ShareCard.Walking(Inputs(3 * 50_000.0 / 60, facts));

        Assert.Equal("Yes", walked.Answer);
        Assert.Equal("warning", walked.Tone);
    }

    [Fact]
    public void Every_row_and_line_on_both_storage_cards_names_its_source()
    {
        var share = ShareCard.View(Inputs(100, ShareFacts()));
        var blob = BlobCard.View(Inputs(100, new Dictionary<string, string?> { ["public_access"] = "none" }), new BlobLines(1e9, 1.8e9, 24_000));

        foreach (var card in new[] { share, blob })
        {
            Assert.All(card.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), $"{card.Title}: {row.Key} names no source"));
            Assert.All(card.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), $"{card.Title}: {line.Label} names no source"));
        }
    }

    [Fact]
    public void The_blob_service_says_no_pictures_yet_and_is_red_when_anything_is_public()
    {
        var lines = new BlobLines(1e9, 1.8e9, 24_000);
        var quiet = BlobCard.View(Inputs(1, new Dictionary<string, string?> { ["public_access"] = "none", ["container_count"] = "1" }), lines);
        Assert.Equal("no pictures yet", quiet.Rows.Single(r => r.Key == "last").Answer);
        Assert.Equal("none yet", quiet.Dials[0].Big);
        Assert.Equal("info", quiet.Rows.Single(r => r.Key == "in").Tone);

        var open = BlobCard.View(Inputs(1, new Dictionary<string, string?> { ["public_access"] = "a container allows it", ["container_count"] = "1" }), lines);
        Assert.Equal("alarm", open.Rows.Single(r => r.Key == "in").Tone);
        Assert.Equal("alarm", open.Readout.Lines.Single(l => l.Label == "Public access").Tone);

        var many = BlobCard.View(Inputs(1, new Dictionary<string, string?> { ["public_access"] = "none", ["pictures_bytes"] = "28400000", ["pictures_count"] = "41" }), lines);
        Assert.Equal("28.4 MB", many.Dials[0].Big);
        Assert.Equal("good", many.Dials[0].Tone);
    }

    [Fact]
    public void A_series_is_one_fact_an_hour_a_value_and_reads_back_as_days()
    {
        var points = new Dictionary<DateTimeOffset, double> { [Noon.AddHours(-2)] = 5, [Noon] = 7 };

        var back = Series.Decode(Series.Encode(points));

        Assert.Equal([(Noon.AddHours(-2), 5d), (Noon, 7d)], back);
        Assert.Equal(12, Series.Daily(back).Single().Value);
        Assert.Equal(5, Series.Percentile([5, 7, 9], 5));
        Assert.Empty(Series.Decode(""));
    }

    [Fact]
    public void The_monitor_answer_split_by_api_becomes_the_table_by_the_hour_and_yesterday()
    {
        string json = """
            {"value":[{"name":{"value":"Transactions"},"timeseries":[
              {"metadatavalues":[{"name":{"value":"apiname"},"value":"ListFiles"}],"data":[{"timeStamp":"2026-10-23T12:00:00Z","total":100},{"timeStamp":"2026-10-24T14:00:00Z","total":40}]},
              {"metadatavalues":[{"name":{"value":"apiname"},"value":"GetFile"}],"data":[{"timeStamp":"2026-10-24T14:30:00Z","total":3}]}]}]}
            """;
        using var answer = JsonDocument.Parse(json);

        var split = Series.Split(answer.RootElement);
        string rows = ShareCard.ApiRows(split, new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero), Noon);

        Assert.Equal("ListFiles\t40\t100\nGetFile\t3\t0", rows);
    }

    [Fact]
    public void A_container_listing_is_its_count_its_bytes_and_its_newest_three()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?><EnumerationResults><Blobs>
            <Blob><Name>a.webp</Name><Properties><Last-Modified>Sat, 24 Oct 2026 14:12:00 GMT</Last-Modified><Content-Length>212000</Content-Length></Properties></Blob>
            <Blob><Name>b.webp</Name><Properties><Last-Modified>Sat, 24 Oct 2026 14:13:00 GMT</Last-Modified><Content-Length>198000</Content-Length></Properties></Blob>
            </Blobs></EnumerationResults>
            """;

        var listing = BlobListing.Parse("blog", xml);

        Assert.Equal((2, 410_000L), (listing.Blobs, listing.Bytes));
        Assert.Equal("b.webp", listing.Newest[0].Name);
    }
}
