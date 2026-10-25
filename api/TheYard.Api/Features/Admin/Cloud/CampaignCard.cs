// The email campaign's card (ADR-118, Decision 6; the review, F14): sends today against the cap and the 30-day
// reply rate on the dials; sends, bounces and replies for sixty minutes; sends a day for thirty days with the
// replies marked; whether it is sending, whether the leads are flowing, whether anyone is answering and what it
// costs; and the Sunday table, the last four weeks by template, weekday, track and proof key. Counts and ages
// only: never a name, a company, an address, a subject or a code. Every count names the day it counts from.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The email campaign's card.</summary>
public static class CampaignCard
{
    /// <summary>The most emails a Central day carries; the Function's CampaignRules.DailyCap, held equal by CampaignCardTests.</summary>
    public const int DailyCap = 10;

    /// <summary>The bounces in one Central day that hold the campaign in shadow; the Function's BounceTrip.Bounces.</summary>
    public const int Trip = 2;

    /// <summary>The sends dial: amber at the cap, never red, since the code cannot pass it.</summary>
    public static readonly (double Amber, double Red) SendsLevels = (100, 101);

    /// <summary>The reply-rate dial: a high rate is good news, so it never goes amber.</summary>
    public static readonly (double Amber, double Red) ReplyLevels = (101, 102);

    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sending"] = "the campaign database's card schema, EmailsSent by Central day",
        ["leads"] = "the campaign database's card schema, Leads and Companies",
        ["answering"] = "the campaign database's card schema, replies and bounces",
        ["cost"] = CardFrame.TheBill,
        ["The switch"] = "the Function's health route",
        ["The cap"] = "the Function's CampaignRules",
        ["Next send"] = "the Function's CentralClock",
        ["The database"] = "functions/JobHunt.Database/JobHunt.Database.sqlproj",
        ["The finder"] = "ADR-118, Steve's ruling of 10/24",
        ["Last bounce"] = "the campaign database's card schema",
        ["Cost"] = CardFrame.TheBill,
        ["Live read"] = CardFrame.ThisProcess,
    };

    /// <summary>A reply rate in words: "12 %", or "none sent".</summary>
    /// <param name="replies">Replies.</param>
    /// <param name="sent">Sends.</param>
    public static string Rate(int replies, int sent) => sent == 0 ? "none sent" : (100.0 * replies / sent).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>The days fact back as days.</summary>
    /// <param name="fact">"2026-10-25,3,0,0,1;...".</param>
    public static IReadOnlyList<(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies)> Days(string? fact) =>
        [.. (fact ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.Split(','))
            .Where(c => c.Length == 5 && DateOnly.TryParseExact(c[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(c => (DateOnly.ParseExact(c[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), Int(c[1]), Int(c[2]), Int(c[3]), Int(c[4])))];

    /// <summary>Draws the card.</summary>
    /// <param name="inputs">The kept minutes and hours.</param>
    public static CloudReport View(ResourceCardInputs inputs)
    {
        var days = Days(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("days"));
        var today = DateOnly.FromDateTime(NightlySlots.Local(inputs.Now));
        string mode = ResourceView.Fact(inputs, "switch", "not read");
        bool shadow = mode != "on";
        double? sentToday = ResourceView.Now(inputs, shadow ? "written_today" : "sent_today");
        var month = days.Where(d => today.DayNumber - d.Day.DayNumber is >= 0 and < 30).ToList();
        int sent30 = month.Sum(d => d.Sent), replies30 = month.Sum(d => d.Replies);
        var dials = new[]
        {
            new CloudDial(shadow ? "Written today, in shadow" : "Sent today", ResourceView.Share(sentToday, DailyCap), ResourceView.Number(sentToday),
                $"of {DailyCap}, the daily cap", SendsLevels.Amber, SendsLevels.Red, null, null, sentToday is null ? "info" : "good"),
            new CloudDial("Reply rate, 30 days", sent30 == 0 ? null : 100.0 * replies30 / sent30, Rate(replies30, sent30),
                $"{replies30} replies of {sent30} sent", ReplyLevels.Amber, ReplyLevels.Red, null, null, "good"),
        };
        var rows = new List<CloudRow> { Sending(inputs, days, today, mode), Leads(inputs, today), Answering(inputs, days, today), CostRow(inputs) };
        var readout = new CloudReadout(
            "The campaign, now",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("The switch", mode, mode == "on" ? "good" : "info"),
                new("The cap", $"{DailyCap} a day; 3, 5 and 10 on the first three days of on", "info"),
                new("Next send", Next(inputs.Now), "info"),
                new("The database", $"{ResourceView.Fact(inputs, "database")}, Basic", "info"),
                new("The finder", "none: public pages only, each address with the page it came from", "info"),
                new("Last bounce", When(ResourceView.Fact(inputs, "last_bounce", "")) ?? "none", "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            "Counts and ages only: the card never shows a name, a company, an address, a subject or a link code. The emails are Steve's approved templates, filled by code.");
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0),
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The email campaign",
            "Azure Functions, 8 AM Central, seven days a week · Azure SQL Basic",
            ResourceView.Resource(inputs),
            dials,
            [Spark(inputs, shadow ? "written_today" : "sent_today", shadow ? "Written, 60 min" : "Sent, 60 min"), Spark(inputs, "bounced_today", "Bounces, 60 min"), Spark(inputs, "replies_today", "Replies, 60 min")],
            Chart(days, today),
            rows,
            readout);
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>Is it sending: today's count, the switch, and the day the count runs from.</summary>
    public static CloudRow Sending(ResourceCardInputs inputs, IReadOnlyList<(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies)> days, DateOnly today, string mode)
    {
        if (ResourceView.Latest(inputs) is null)
        {
            return new CloudRow("sending", "Is it sending?", "not read yet", [new("today's emails, the switch and the day the counts start", null)], "info");
        }
        var day = days.FirstOrDefault(d => d.Day == today);
        string? from = When(ResourceView.Fact(inputs, "first_written", ""));
        int bounces = day.Bounced;
        int ready = Int(ResourceView.Fact(inputs, "with_address", "0"));
        bool lateWithNone = mode == "on" && day.Sent == 0 && ready > 0 && NightlySlots.Local(inputs.Now).Hour >= 9;
        string tone = bounces >= Trip || lateWithNone ? "alarm" : "good";
        string answer = mode == "on" ? $"{day.Sent} sent today" : mode is "shadow" ? $"Shadow: {day.Shadow} written today" : $"Off ({mode})";
        string why = bounces >= Trip ? $" · {bounces} bounces today: held in shadow until a person clears campaign/switch.log"
            : lateWithNone ? $" · none sent by 9 AM with {ready} ready" : "";
        return new CloudRow("sending", "Is it sending?", answer,
            [new($"the switch is {mode} · at 8 AM Central, seven days a week, at most {DailyCap} · counting from {from ?? "the first email, not yet written"}{why}", null)], tone);
    }

    /// <summary>Are the leads flowing: open, with an address, waiting on one, blocked, and the board scan.</summary>
    public static CloudRow Leads(ResourceCardInputs inputs, DateOnly today)
    {
        string open = ResourceView.Fact(inputs, "open", "");
        if (open == "")
        {
            return new CloudRow("leads", "Are the leads flowing?", "not read yet", [new("open leads, with an address, waiting on one", null)], "info");
        }
        string scan = When(ResourceView.Fact(inputs, "last_scan", "")) is { } at ? $"boards scanned {at}" : "no board scanned yet";
        return new CloudRow("leads", "Are the leads flowing?", $"{open} open",
            [new($"{ResourceView.Fact(inputs, "with_address", "0")} with an address from a public page · {ResourceView.Fact(inputs, "waiting_address", "0")} waiting on one (no finder) · {ResourceView.Fact(inputs, "blocked", "0")} blocked by a rule · {ResourceView.Fact(inputs, "no_mx", "0")} with no mail records · {ResourceView.Fact(inputs, "companies", "0")} companies, {ResourceView.Fact(inputs, "boards", "0")} with a public board · {scan}", null)], "info");
    }

    /// <summary>Is anyone answering: replies in 7 and 30 days, bounces, stop requests, and the Sunday table.</summary>
    public static CloudRow Answering(ResourceCardInputs inputs, IReadOnlyList<(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies)> days, DateOnly today)
    {
        if (ResourceView.Latest(inputs) is null)
        {
            return new CloudRow("answering", "Is anyone answering?", "not read yet", [new("replies, bounces and stop requests", null)], "info");
        }
        int Sum(int window, Func<(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies), int> pick) => days.Where(d => today.DayNumber - d.Day.DayNumber is >= 0 && today.DayNumber - d.Day.DayNumber < window).Sum(pick);
        int sent30 = Sum(30, d => d.Sent);
        return new CloudRow("answering", "Is anyone answering?", sent30 == 0 ? "Nothing sent yet" : $"{Sum(30, d => d.Replies)} replies in 30 days",
            [new($"{Sum(7, d => d.Replies)} in 7 days · {Sum(30, d => d.Bounced)} bounced in 30 days · {ResourceView.Fact(inputs, "stopped", "0")} companies asked to stop · the table is the last four weeks by template, weekday of send, track and proof key", null)], "info")
        { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("weeks"), "Template", "Weekday", "Proof", "Replies of sent") };
    }

    /// <summary>What it costs: the database's month on the bill, and the parts that cost nothing.</summary>
    private static CloudRow CostRow(ResourceCardInputs inputs) =>
        new("cost", "Is it costing anything?", inputs.CostMonth is { } dollars ? $"${dollars.ToString("0.00", CultureInfo.InvariantCulture)} this month" : "not on the bill yet",
            [new("the Basic database, a flat price a month on the bill · the Function's runs inside its free grant (the nightly card) · Gmail at ten a day, inside its free limit · no finder, no key, no account", null)], "info");

    /// <summary>Emails a day for thirty days against the cap, the replies marked.</summary>
    private static CloudChart Chart(IReadOnlyList<(DateOnly Day, int Sent, int Shadow, int Bounced, int Replies)> days, DateOnly today)
    {
        var window = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        long Ms(DateOnly day) => new DateTimeOffset(day.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero).ToUnixTimeMilliseconds();
        var byDay = days.ToDictionary(d => d.Day);
        var sent = window.Select(d => new CloudPoint(Ms(d), byDay.TryGetValue(d, out var r) ? 100.0 * r.Sent / DailyCap : 0)).ToList();
        var written = window.Select(d => new CloudPoint(Ms(d), byDay.TryGetValue(d, out var r) ? 100.0 * r.Shadow / DailyCap : 0)).ToList();
        var marks = window.Where(d => byDay.TryGetValue(d, out var r) && r.Replies > 0).Select(d => new CloudMark(Ms(d), "event", $"{byDay[d].Replies} replies to the {d:ddd d MMM} emails")).ToList();
        return new CloudChart("Emails a day, last 30 days", [new("Sent", sent), new("Written in shadow", written)], marks, 100,
            "teal sent · green written in shadow · red the daily cap of ten · each red mark is a day whose emails drew replies; weekends count like weekdays (Steve's ruling, seven days a week)",
            Ms(window[0]));
    }

    private static CloudSpark Spark(ResourceCardInputs inputs, string reading, string name) => ResourceView.Spark(inputs, reading, name, DailyCap);

    /// <summary>The next 8 o'clock hour Central, in words.</summary>
    /// <param name="now">The moment, UTC.</param>
    public static string Next(DateTimeOffset now)
    {
        var hour = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, now.UtcDateTime.Day, now.UtcDateTime.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        while (NightlySlots.Local(hour).Hour != 8)
        {
            hour = hour.AddHours(1);
        }
        return NightlySlots.Local(hour).ToString("ddd d MMM", CultureInfo.InvariantCulture) + ", 8:00 AM Central";
    }

    private static string? When(string fact) =>
        DateTimeOffset.TryParse(fact, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? NightlySlots.Local(at).ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture) + " Central" : null;

    private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
