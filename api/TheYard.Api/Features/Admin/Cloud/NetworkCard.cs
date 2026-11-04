// The network log's card (ADR: The network log drafts, Steve sends): conversations started this week against the
// four weeks before, and the follow-up drafts waiting, on the dials; drafts waiting for sixty minutes; conversations
// started a week for five weeks; whether the pass is logging, where the follow-ups stand and what it costs. Counts
// only: never a name, a company, an address, a subject or a code.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The network log's card.</summary>
public static class NetworkCard
{
    /// <summary>The most follow-ups one Monday drafts; the Function's DueRule.MondayCap, held equal by NetworkCardTests.</summary>
    public const int MondayCap = 15;

    /// <summary>The states a row can be in, in the order the card says them; the Function's ConversationState.</summary>
    public static readonly IReadOnlyList<string> States = ["Open", "Drafted", "Quiet", "Stopped", "Closed"];

    /// <summary>The week dial: a busy week is good news, so it never goes amber.</summary>
    public static readonly (double Amber, double Red) WeekLevels = (1000, 1001);

    /// <summary>The drafts dial: amber at a full Monday still waiting, never red.</summary>
    public static readonly (double Amber, double Red) DraftLevels = (100, 101);

    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["logging"] = "the card schema's Network view, conversations by the week they started",
        ["follow-ups"] = "the card schema's Network view, rows by state",
        ["cost"] = CardFrame.TheBill,
        ["The switch"] = "the Function's health route",
        ["Next pass"] = "the Function's CentralClock",
        ["The window"] = "the Function's DueRule",
        ["The database"] = "functions/JobHunt.Database/JobHunt.Database.sqlproj",
        ["Cost"] = CardFrame.TheBill,
        ["Live read"] = CardFrame.ThisProcess,
    };

    /// <summary>The Monday the ISO week of a Central day starts on.</summary>
    /// <param name="day">The Central day.</param>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The weeks fact back as weeks.</summary>
    /// <param name="fact">"2026-11-02,4;2026-10-26,7".</param>
    public static IReadOnlyList<(DateOnly Monday, int Started)> Weeks(string? fact) =>
        [.. (fact ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.Split(','))
            .Where(c => c.Length == 2 && DateOnly.TryParseExact(c[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(c => (DateOnly.ParseExact(c[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), Int(c[1])))];

    /// <summary>Draws the card.</summary>
    /// <param name="inputs">The kept minutes and hours.</param>
    public static CloudReport View(ResourceCardInputs inputs)
    {
        var weeks = Weeks(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("weeks"));
        var monday = Monday(DateOnly.FromDateTime(NightlySlots.Local(inputs.Now)));
        int thisWeek = weeks.Where(w => w.Monday == monday).Sum(w => w.Started);
        var before = Enumerable.Range(1, 4).Select(i => weeks.Where(w => w.Monday == monday.AddDays(-7 * i)).Sum(w => w.Started)).ToList();
        double average = before.Average();
        string mode = ResourceView.Fact(inputs, "switch", "not read");
        double? drafted = ResourceView.Now(inputs, "drafted");
        var dials = new[]
        {
            new CloudDial("Started this week", average > 0 ? 100.0 * thisWeek / average : null, ResourceView.Latest(inputs) is null ? "not read" : thisWeek.ToString(CultureInfo.InvariantCulture),
                $"against {average.ToString("0.#", CultureInfo.InvariantCulture)} a week over the four before", WeekLevels.Amber, WeekLevels.Red, null, null, "good"),
            new CloudDial("Drafts waiting", ResourceView.Share(drafted, MondayCap), ResourceView.Number(drafted), $"of at most {MondayCap} a Monday, under Follow-up/Ready", DraftLevels.Amber, DraftLevels.Red, null, null, drafted is null ? "info" : "good"),
        };
        var rows = new List<CloudRow> { Logging(inputs, weeks, monday, mode), FollowUps(inputs), CostRow(inputs) };
        var readout = new CloudReadout(
            "The network log, now",
            ResourceView.Uptime(inputs),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("The switch", mode, mode == "on" ? "good" : "info"),
                new("Next pass", Next(inputs.Now), "info"),
                new("The window", $"21 to 45 days after the last exchange, at most {MondayCap} a Monday; nothing sends, Steve presses Send", "info"),
                new("The database", $"{ResourceView.Fact(inputs, "database")}, Basic", "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            "Counts only: the card never shows a name, a company, an address, a subject or a link code. The drafts are FOLLOWUP-v1, filled by code.");
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0),
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "The network log",
            "Azure Functions, 6 AM Central, once a day · Azure SQL Basic",
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "drafted", "Drafts waiting, 60 min", MondayCap), ResourceView.Spark(inputs, "open", "Open, 60 min", null)],
            Chart(weeks, monday),
            rows,
            readout);
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>Is it logging: this week's new conversations, the switch, and the weeks before.</summary>
    public static CloudRow Logging(ResourceCardInputs inputs, IReadOnlyList<(DateOnly Monday, int Started)> weeks, DateOnly monday, string mode)
    {
        if (ResourceView.Latest(inputs) is null)
        {
            return new CloudRow("logging", "Is it logging?", "not read yet", [new("conversations started a week and the switch", null)], "info");
        }
        int thisWeek = weeks.Where(w => w.Monday == monday).Sum(w => w.Started);
        string answer = mode is "on" or "shadow" ? $"{thisWeek} started this week" : $"Off ({mode})";
        string last = string.Join(", ", Enumerable.Range(1, 4).Select(i => weeks.Where(w => w.Monday == monday.AddDays(-7 * i)).Sum(w => w.Started).ToString(CultureInfo.InvariantCulture)));
        return new CloudRow("logging", "Is it logging?", answer,
            [new($"the switch is {mode} · once a day in the 6 AM hour Central · the four weeks before, newest first: {last}", null)], mode is "on" or "shadow" ? "good" : "info");
    }

    /// <summary>Where the follow-ups stand: the rows by state.</summary>
    public static CloudRow FollowUps(ResourceCardInputs inputs)
    {
        string open = ResourceView.Fact(inputs, "open", "");
        if (open == "")
        {
            return new CloudRow("follow-ups", "Where do the follow-ups stand?", "not read yet", [new("open, drafted, quiet, stopped and closed", null)], "info");
        }
        return new CloudRow("follow-ups", "Where do the follow-ups stand?", $"{ResourceView.Fact(inputs, "drafted", "0")} drafted, waiting for Steve",
            [new($"{open} open · {ResourceView.Fact(inputs, "quiet", "0")} quiet after a follow-up, never drafted again · {ResourceView.Fact(inputs, "stopped", "0")} asked to stop · {ResourceView.Fact(inputs, "closed", "0")} closed (stale, bounced or declined)", null)], "info");
    }

    /// <summary>What it costs: nothing of its own; its tables live in the site's Basic database, already on the Azure SQL card.</summary>
    private static CloudRow CostRow(ResourceCardInputs inputs) =>
        new("cost", "Is it costing anything?", "nothing of its own this month",
            [new((inputs.CostMonth is { } dollars ? $"shares the site's Basic database, already on the bill (${dollars.ToString("0.00", CultureInfo.InvariantCulture)} this month on the Azure SQL card)" : "shares the site's Basic database, already on the bill on the Azure SQL card")
                + " · one Function run a day inside its free grant · Gmail reads and drafts inside its free limit", null)], "info");

    /// <summary>Conversations started a week, five weeks.</summary>
    private static CloudChart Chart(IReadOnlyList<(DateOnly Monday, int Started)> weeks, DateOnly monday)
    {
        var window = Enumerable.Range(0, 5).Select(i => monday.AddDays(7 * (i - 4))).ToList();
        long Ms(DateOnly day) => new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero).ToUnixTimeMilliseconds();
        int top = Math.Max(1, window.Max(d => weeks.Where(w => w.Monday == d).Sum(w => w.Started)));
        var points = window.Select(d => new CloudPoint(Ms(d), 100.0 * weeks.Where(w => w.Monday == d).Sum(w => w.Started) / top)).ToList();
        return new CloudChart("Conversations started a week, five weeks", [new("Started", points)], [], 101,
            $"teal conversations whose first two-way exchange fell in the ISO week, scaled to the busiest week ({top})", Ms(window[0]));
    }

    /// <summary>The next 6 o'clock hour Central, in words.</summary>
    /// <param name="now">The moment, UTC.</param>
    public static string Next(DateTimeOffset now)
    {
        var hour = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, now.UtcDateTime.Day, now.UtcDateTime.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
        while (NightlySlots.Local(hour).Hour != 6)
        {
            hour = hour.AddHours(1);
        }
        return NightlySlots.Local(hour).ToString("ddd d MMM", CultureInfo.InvariantCulture) + ", 6:00 AM Central";
    }

    private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
