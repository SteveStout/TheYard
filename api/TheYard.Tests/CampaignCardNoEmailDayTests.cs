using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// noemailcard1127: a day J:\dispatch\NO-EMAIL-DAYS.txt lists reads as a hold on the campaign card, never as a silent
/// morning, and "Next send" skips the listed mornings; both from the Function's health route, which reads the mail
/// gate's own file. A route that does not say, a stale hour or a false still reads a silent morning red, as before.
/// Fakes only (RULE 79): the health route is a delegate, the counts a hand-written reader.
/// </summary>
public sealed class CampaignCardNoEmailDayTests
{
    /// <summary>20:27 CST Friday 27 November 2026, when Steve asked why the card was red.</summary>
    private static readonly DateTimeOffset Asked = new(2026, 11, 28, 2, 27, 0, TimeSpan.Zero);

    /// <summary>10:00 CST Wednesday 23 December 2026, the last open morning before nine listed ones.</summary>
    private static readonly DateTimeOffset Eve = new(2026, 12, 23, 16, 0, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(DateTimeOffset now, IReadOnlyDictionary<string, string?> facts, double sent = 0, double bounced = 0, DateTimeOffset? hourAt = null)
    {
        var values = new Dictionary<string, double?> { ["sent_today"] = sent, ["written_today"] = 0, ["bounced_today"] = bounced, ["replies_today"] = 0 };
        var lastHour = Enumerable.Range(0, 60).Select(i => new ResourceMinute(now.AddMinutes(i - 60), values, "test")).ToList();
        var bucket = new ResourceBucket(now.AddMinutes(-5), 5, values, values, values, "test");
        IReadOnlyList<ResourceHour> hours = [new ResourceHour(hourAt ?? now.AddMinutes(-27), "db", null, facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), now, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket], hours, 1.02);
    }

    /// <summary>The card's facts as the 20:00 CST hour kept them on 11/27: the switch on, nothing sent, six leads ready.</summary>
    private static Dictionary<string, string?> Facts(string days, string? listed, string? next, string withAddress = "6") => new(StringComparer.Ordinal)
    {
        ["switch"] = "on",
        ["days"] = days,
        ["open"] = "128",
        ["with_address"] = withAddress,
        ["waiting_address"] = "11",
        ["blocked"] = "0",
        ["no_mx"] = "0",
        ["companies"] = "90",
        ["stopped"] = "0",
        ["boards"] = "145",
        ["first_written"] = "2026-11-12T14:00:10+00:00",
        ["weeks"] = "",
        ["no_email_today"] = listed,
        ["next_email_day"] = next,
    };

    private static string Line(CloudReport card, string label) => card.Readout.Lines.Single(l => l.Label == label).Value;

    [Fact]
    public void The_27th_replayed_reads_held_in_info_and_the_next_send_is_Saturday()
    {
        var card = CampaignCard.View(Inputs(Asked, Facts("2026-11-26,0,0,0,0;2026-11-27,0,0,0,0", "true", "2026-11-28")));
        var row = card.Rows[0];

        Assert.Equal("info", row.Tone);
        Assert.Equal("Held: no-email day", row.Answer);
        Assert.Equal(@"the switch is on · today is listed in J:\dispatch\NO-EMAIL-DAYS.txt, so the gate holds every send · next send Sat 28 Nov, 8:00 AM Central", row.Detail[0].Text);
        Assert.Equal("Sat 28 Nov, 8:00 AM Central", Line(card, "Next send"));
        Assert.Equal("today, listed", Line(card, "No-email day"));
        Assert.Equal(@"J:\dispatch\NO-EMAIL-DAYS.txt, read by the Function's mail gate", card.Readout.Lines.Single(l => l.Label == "No-email day").Source);
        Assert.Equal("info", card.Readout.Lines.Single(l => l.Label == "No-email day").Tone);
        Assert.DoesNotContain(card.Rows, r => r.Tone == "alarm");
    }

    [Theory]
    [InlineData("false", "2026-11-28")]
    [InlineData(null, null)]
    public void The_same_morning_not_listed_or_not_read_is_still_red(string? listed, string? next)
    {
        var card = CampaignCard.View(Inputs(Asked, Facts("2026-11-27,0,0,0,0", listed, next)));

        Assert.Equal("alarm", card.Rows[0].Tone);
        Assert.Equal("none sent by 9 AM, 6 ready", card.Rows[0].Answer);
        Assert.Equal(listed is null ? "not read" : "not today", Line(card, "No-email day"));
    }

    [Fact]
    public void A_listed_fact_kept_on_another_day_quiets_nothing()
    {
        // The newest hour is from 23:00 CST on 11/26: its "true" was about the 26th, so the 27th's silence is red.
        var card = CampaignCard.View(Inputs(Asked, Facts("2026-11-27,0,0,0,0", "true", "2026-11-28"), hourAt: new DateTimeOffset(2026, 11, 27, 5, 0, 0, TimeSpan.Zero)));

        Assert.Equal("alarm", card.Rows[0].Tone);
        Assert.Equal("not read", Line(card, "No-email day"));
    }

    [Fact]
    public void Two_bounces_on_a_listed_day_are_still_red()
    {
        var card = CampaignCard.View(Inputs(Asked, Facts("2026-11-27,0,0,2,0", "true", "2026-11-28"), bounced: 2));

        Assert.Equal("alarm", card.Rows[0].Tone);
        Assert.Equal("2 bounces today, 0 sent", card.Rows[0].Answer);
    }

    [Fact]
    public void On_the_23rd_after_its_send_the_next_send_skips_the_nine_listed_mornings()
    {
        // The Function reads the real file at 10:00 CST on 12/23 as 2027-01-02 (MergerWatchMailDaysTests).
        var card = CampaignCard.View(Inputs(Eve, Facts("2026-12-23,10,0,0,0", "false", "2027-01-02"), sent: 10));

        Assert.Equal("Sat 2 Jan, 8:00 AM Central", Line(card, "Next send"));
        Assert.Equal("not today", Line(card, "No-email day"));
        Assert.Equal(("10 sent today", "good"), (card.Rows[0].Answer, card.Rows[0].Tone));
        // No day from the route, or one already behind the next 8 o'clock, names the next 8 o'clock as before.
        Assert.Equal(CampaignCard.Next(Eve), CampaignCard.Next(Eve, null));
        Assert.Equal(CampaignCard.Next(Eve), CampaignCard.Next(Eve, "2026-12-23"));
        Assert.Equal("Thu 24 Dec, 8:00 AM Central", CampaignCard.Next(Eve));
    }

    [Fact]
    public async Task The_source_keeps_both_fields_from_the_health_read_and_null_when_it_does_not_answer()
    {
        var read = new CampaignCountsRead(true, null, [], null, []);
        Task<(string, string?)?> Resource(CancellationToken _) => Task.FromResult<(string, string?)?>(("/db", null));
        var answered = new CampaignSource(new Counts(read), _ => Task.FromResult<FunctionHealth?>(new FunctionHealth(new Dictionary<string, string> { ["EMAIL_CAMPAIGN"] = "on" }, "true", "2026-11-28")), Resource, "campaign-db");
        var silent = new CampaignSource(new Counts(read), _ => Task.FromResult<FunctionHealth?>(null), Resource, "campaign-db");

        var hour = await answered.HourAsync(Asked, Asked.AddHours(-1), CancellationToken.None);
        var none = await silent.HourAsync(Asked, Asked.AddHours(-1), CancellationToken.None);

        Assert.Equal(("on", "true", "2026-11-28"), (hour.Facts["switch"], hour.Facts["no_email_today"], hour.Facts["next_email_day"]));
        Assert.Null(none.Facts["switch"]);
        Assert.Null(none.Facts["no_email_today"]);
        Assert.Null(none.Facts["next_email_day"]);
    }

    [Theory]
    [InlineData("""{"switches":{"EMAIL_CAMPAIGN":"on"},"no_email_today":true,"next_email_day":"2026-11-28"}""", "true", "2026-11-28")]
    [InlineData("""{"switches":{"EMAIL_CAMPAIGN":"on"},"no_email_today":false,"next_email_day":"2026-12-24"}""", "false", "2026-12-24")]
    [InlineData("""{"switches":{"EMAIL_CAMPAIGN":"on"},"no_email_today":null,"next_email_day":null}""", null, null)]
    [InlineData("""{"switches":{"EMAIL_CAMPAIGN":"on"}}""", null, null)]
    [InlineData("""{"no_email_today":"yes","next_email_day":"28 Nov"}""", null, null)]
    public void The_health_answer_keeps_each_field_only_in_its_own_shape(string json, string? listed, string? next)
    {
        using var answer = JsonDocument.Parse(json);
        var health = FunctionHealth.From(answer.RootElement);

        Assert.Equal((listed, next), (health.NoEmailToday, health.NextEmailDay));
    }

    private sealed class Counts(CampaignCountsRead read) : ICampaignCounts
    {
        public Task<CampaignCountsRead> ReadAsync(CancellationToken cancellation) => Task.FromResult(read);
    }
}
