using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The email campaign's card (ADR-118, Decision 6; the review, F14): the sends dial against the cap the Function
/// holds, the reply rate, the four questions with nothing read and with reads, red at two bounces in a day and at
/// no send by 9 AM with leads ready, the Sunday table, every row and line sourced, the count's start date named,
/// and nothing on the wire that could be a name, an address or a code; the source's minute and hour from a fake
/// reader; the connection string built from the site's own.
/// </summary>
public sealed class CampaignCardTests
{
    // 10:00 CDT on Sunday 25 October 2026.
    private static readonly DateTimeOffset Ten = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(IReadOnlyDictionary<string, string?>? facts, double sent = 0, double written = 0, double bounced = 0)
    {
        var values = new Dictionary<string, double?> { ["sent_today"] = sent, ["written_today"] = written, ["bounced_today"] = bounced, ["replies_today"] = 0 };
        var lastHour = Enumerable.Range(0, 60).Select(i => new ResourceMinute(Ten.AddMinutes(i - 60), values, "test")).ToList();
        var bucket = new ResourceBucket(Ten.AddMinutes(-5), 5, values, values, values, "test");
        IReadOnlyList<ResourceHour> hours = facts is null ? [] : [new ResourceHour(Ten.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Sql/servers/sql-theyard-ss-westus3/databases/sqldb-jobhunt-ss", null, facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Ten, new PlanHistoryAvailability(true, "kept in the test"), facts is null ? [] : lastHour, [bucket], [bucket], hours, 1.02);
    }

    private static Dictionary<string, string?> Facts(string mode, string days, string withAddress = "4") => new(StringComparer.Ordinal)
    {
        ["switch"] = mode,
        ["days"] = days,
        ["open"] = "12",
        ["with_address"] = withAddress,
        ["waiting_address"] = "8",
        ["blocked"] = "1",
        ["no_mx"] = "0",
        ["companies"] = "15",
        ["stopped"] = "1",
        ["boards"] = "9",
        ["last_scan"] = "2026-10-25T13:00:04+00:00",
        ["first_written"] = "2026-10-25T13:00:10+00:00",
        ["weeks"] = "B v1, track B\tSunday\tai\t1 of 3 (33 %)",
    };

    [Fact]
    public void The_cap_is_the_Function_s_own_and_the_trip_is_its_two_bounces()
    {
        string rules = File.ReadAllText(Path.Combine(Repo.Root(), "functions", "JobHunt.Domain", "Features", "Campaign", "CampaignRules.cs"));
        string trip = File.ReadAllText(Path.Combine(Repo.Root(), "functions", "JobHunt.Domain", "Features", "Campaign", "DeliveryFailure.cs"));
        Assert.Contains($"public const int DailyCap = {CampaignCard.DailyCap};", rules, StringComparison.Ordinal);
        Assert.Contains($"public const int Bounces = {CampaignCard.Trip};", trip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cost_row_claims_nothing_of_its_own_and_names_the_shared_database()
    {
        var cost = CampaignCard.View(Inputs(Facts("shadow", "2026-10-25,0,6,0,0"), written: 6)).Rows.Single(r => r.Key == "cost");

        Assert.Equal("nothing of its own this month", cost.Answer);
        Assert.StartsWith("shares the site's Basic database, already on the bill ($1.02 this month on the Azure SQL card)", cost.Detail[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("a flat price", cost.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_read_every_question_says_so_and_nothing_is_red()
    {
        var card = CampaignCard.View(Inputs(null));

        Assert.Equal(["sending", "leads", "answering", "cost"], card.Rows.Select(r => r.Key));
        Assert.All(card.Rows.Take(3), row => Assert.StartsWith("not read", row.Answer, StringComparison.Ordinal));
        Assert.DoesNotContain(card.Rows, row => row.Tone == "alarm");
        Assert.Equal("The email campaign", card.Title);
    }

    [Fact]
    public void In_shadow_the_dial_counts_the_would_sends_and_the_count_names_its_first_day()
    {
        var card = CampaignCard.View(Inputs(Facts("shadow", "2026-10-25,0,6,0,0"), written: 6));

        Assert.Equal(("Written today, in shadow", "6", 60d), (card.Dials[0].Name, card.Dials[0].Big, card.Dials[0].Percent!.Value));
        Assert.Equal("Shadow: 6 written today", card.Rows[0].Answer);
        Assert.Contains("counting from Sun 25 Oct, 08:00 Central", card.Rows[0].Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("12 open", card.Rows[1].Answer);
        Assert.Contains("4 with an address from a public page · 8 waiting on one (no finder)", card.Rows[1].Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("Nothing sent yet", card.Rows[2].Answer);
        Assert.Equal("good", card.Rows[0].Tone);
    }

    [Fact]
    public void On_it_counts_sends_and_the_reply_rate_over_thirty_days()
    {
        var card = CampaignCard.View(Inputs(Facts("on", "2026-10-23,3,0,0,1;2026-10-24,5,0,0,1;2026-10-25,10,0,0,0"), sent: 10));

        Assert.Equal(("Sent today", "10"), (card.Dials[0].Name, card.Dials[0].Big));
        Assert.Equal(("2 of 18 sent", "11 %"), (card.Dials[1].Absolute.Replace(" replies", "", StringComparison.Ordinal), card.Dials[1].Big));
        Assert.Equal("10 sent today", card.Rows[0].Answer);
        Assert.Equal("2 replies in 30 days", card.Rows[2].Answer);
        Assert.Equal(["Template", "Weekday", "Proof", "Replies of sent"], card.Rows[2].Table.Columns);
    }

    [Fact]
    public void Two_bounces_in_a_day_or_no_send_by_nine_with_leads_ready_is_red()
    {
        var bounced = CampaignCard.View(Inputs(Facts("on", "2026-10-25,4,0,2,0"), sent: 4, bounced: 2));
        var silent = CampaignCard.View(Inputs(Facts("on", "2026-10-24,10,0,0,0"), sent: 0));
        var silentNoLeads = CampaignCard.View(Inputs(Facts("on", "2026-10-24,10,0,0,0", withAddress: "0"), sent: 0));

        Assert.Equal("alarm", bounced.Rows[0].Tone);
        Assert.Contains("2 bounces today: held in shadow until a person clears campaign/switch.log", bounced.Rows[0].Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("alarm", silent.Rows[0].Tone);
        Assert.Contains("none sent by 9 AM with 4 ready", silent.Rows[0].Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("good", silentNoLeads.Rows[0].Tone);
    }

    [Fact]
    public void Every_row_and_line_names_its_source_and_the_wire_carries_no_address_or_code()
    {
        var card = CampaignCard.View(Inputs(Facts("shadow", "2026-10-25,0,6,0,0"), written: 6));

        Assert.All(card.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
        Assert.All(card.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), line.Label));
        string wire = JsonSerializer.Serialize(card);
        Assert.DoesNotContain("@", wire, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("r=[a-z0-9]{6}"), wire);
        Assert.Contains("Sun 25 Oct, 8:00 AM Central", CampaignCard.Next(Ten.AddHours(-3)), StringComparison.Ordinal);
        Assert.Contains("Mon 26 Oct, 8:00 AM Central", CampaignCard.Next(Ten), StringComparison.Ordinal);
        Assert.Contains("Mon 2 Nov, 8:00 AM Central", CampaignCard.Next(new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero)), StringComparison.Ordinal);
    }

    private sealed class FakeCounts(CampaignCountsRead read) : ICampaignCounts
    {
        public Task<CampaignCountsRead> ReadAsync(CancellationToken cancellation) => Task.FromResult(read);
    }

    [Fact]
    public async Task The_source_keeps_today_s_four_counts_a_minute_and_the_tables_an_hour()
    {
        var read = new CampaignCountsRead(true, null,
            [new CampaignDay(new DateOnly(2026, 10, 24), 0, 9, 0, 0), new CampaignDay(new DateOnly(2026, 10, 25), 0, 6, 0, 0)],
            new CampaignLeadCounts(12, 4, 8, 1, 0, 0, 15, 1, 9, null, Ten.AddHours(-2), null, null),
            [new CampaignWeekRow("B v1", "Sunday", "B", "ai", 3, 1)]);
        var source = new CampaignSource(new FakeCounts(read), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(("/db", null)), "campaign-db");

        var minute = (await source.MinutesAsync(Ten.AddMinutes(-1), Ten, "v", CancellationToken.None)).Single();
        var hour = await source.HourAsync(Ten, Ten.AddHours(-1), CancellationToken.None);
        var unread = await new CampaignSource(new FakeCounts(CampaignCountsRead.Unread("not configured here")), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "campaign-db")
            .HourAsync(Ten, Ten.AddHours(-1), CancellationToken.None);

        Assert.Equal((6d, 0d), (minute.Values["written_today"]!.Value, minute.Values["sent_today"]!.Value));
        Assert.Equal("2026-10-24,0,9,0,0;2026-10-25,0,6,0,0", hour.Facts["days"]);
        Assert.Equal("12", hour.Facts["open"]);
        Assert.Equal("B v1, track B\tSunday\tai\t1 of 3 (33 %)", hour.Facts["weeks"]);
        Assert.Equal("/db", hour.ResourceId);
        Assert.Equal("not configured here", unread.Facts[ResourceView.MissingFact]);
    }

    [Theory]
    [InlineData("Server=tcp:sql-theyard-ss-westus3.database.windows.net,1433;Initial Catalog=sqldb-theyard-ss-basic;Authentication=Active Directory Managed Identity;User Id=abc;", true)]
    [InlineData("Data Source=/tmp/yard.db", false)]
    [InlineData(null, false)]
    public void The_campaign_s_connection_is_the_site_s_own_with_the_catalog_changed(string? site, bool built)
    {
        string? campaign = SqlCampaignCounts.For(site, "sqldb-theyard-ss-basic");
        Assert.Equal(built, campaign is not null);
        if (campaign is not null)
        {
            Assert.Contains("Initial Catalog=sqldb-theyard-ss-basic", campaign, StringComparison.Ordinal);
            Assert.Contains("ActiveDirectoryManagedIdentity", campaign.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", campaign, StringComparison.OrdinalIgnoreCase);
        }
    }
}
