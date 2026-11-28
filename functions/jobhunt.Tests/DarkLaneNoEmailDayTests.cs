using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// noemailcard1127: an 8 o'clock pass the no-email-day gate held (no_email_day on its held list, nothing sent) is a held
/// run, not an empty one. A held day neither breaks a dark streak nor lengthens it: it is skipped. Over the nine listed
/// days from 12/24 the campaign writes no DARK line; three unlisted empty mornings still write one. Strings only (RULE 79).
/// </summary>
public sealed class DarkLaneNoEmailDayTests
{
    private static readonly SendingLane Campaign = DarkLanes.Lanes.Single(l => l.Key == "campaign");

    /// <summary>The 11/27 08:00:16 pass line in J:\campaign\pass.log, its held list cut short, on another day.</summary>
    private static string Held(DateOnly day) =>
        $"{day:yyyy-MM-dd} 08:00:16 scan: scanned 01:00 | track A: 11 applications in 7 days | on: 128 open, 53 eligible, 0 sent of 10 left today | held: 51 follow-up, 11 no address, 1 live thread, Count 3259, 1 no_email_day, 1 shared mailbox under a person's name\n";

    /// <summary>The same pass on an open day that sent nothing with leads ready.</summary>
    private static string Empty(DateOnly day) =>
        $"{day:yyyy-MM-dd} 08:00:12 scan: scanned 01:00 | on: 128 open, 53 eligible, 0 sent of 10 left today | held: 51 follow-up, 11 no address\n";

    /// <summary>The 12/23 send, as the tracker row the campaign writes.</summary>
    private const string Sent = "Count,Person,Company,Communication,Date,Phone/Email,Notes\n4100,Ann Lee,Acme,Email,12/23/2026,ann@acme.test,\"CAMPAIGN EMAIL SENT 8:00 AM CST 12/23\"\n";

    private static readonly DateOnly Eve = new(2026, 12, 24);

    private static IEnumerable<DateOnly> Listed() => Enumerable.Range(0, 9).Select(i => Eve.AddDays(i));

    private static DarkReads Reads(string passLog, string tracker = Sent) =>
        new(string.Empty, new Dictionary<string, string>(), [], string.Empty, tracker, passLog, new HashSet<string>(), new HashSet<string>());

    [Fact]
    public void The_nine_listed_days_from_Christmas_Eve_write_no_dark_line()
    {
        string pass = string.Concat(Listed().Select(Held));
        // The 07:30 card on 12/27 (the three passes before it held), on 1/1 and on 1/2 before its pass.
        foreach (var today in new[] { new DateOnly(2026, 12, 27), new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 2) })
        {
            Assert.Null(DarkLanes.Line(Campaign, Reads(pass), today));
            Assert.DoesNotContain(DarkLanes.Lines(Reads(pass), today), l => l.Contains("Campaign emails", StringComparison.Ordinal));
        }
        // With no send in the window at all, held mornings alone are still nothing due.
        Assert.Null(DarkLanes.Line(Campaign, Reads(pass, tracker: string.Empty), new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void A_held_day_neither_breaks_nor_lengthens_a_streak()
    {
        string listed = string.Concat(Listed().Select(Held));
        // 1/2 empty after nine held mornings: one counted day, not ten.
        Assert.Null(DarkLanes.Line(Campaign, Reads(listed + Empty(new DateOnly(2027, 1, 2))), new DateOnly(2027, 1, 2)));
        // 1/2 to 1/4 empty: the streak is three counted days, read past the held ones, and says four on 1/5.
        string three = listed + Empty(new DateOnly(2027, 1, 2)) + Empty(new DateOnly(2027, 1, 3)) + Empty(new DateOnly(2027, 1, 4));
        Assert.StartsWith("DARK: Campaign emails (8 AM): 0 sent in 4 days. Why: the last run read \"53 eligible, 0 sent\".", DarkLanes.Line(Campaign, Reads(three), new DateOnly(2027, 1, 5)));
        // Two empty mornings before the listed days and one after: the held ones do not break the streak either.
        string around = Empty(new DateOnly(2026, 12, 22)) + Empty(new DateOnly(2026, 12, 23)) + listed + Empty(new DateOnly(2027, 1, 2));
        Assert.StartsWith("DARK: Campaign emails (8 AM): 0 sent in 3 days.", DarkLanes.Line(Campaign, Reads(around, tracker: SentOn("12/21/2026")), new DateOnly(2027, 1, 2)));
    }

    [Fact]
    public void Three_unlisted_empty_days_are_still_dark()
    {
        var monday = new DateOnly(2026, 11, 30);
        string pass = Empty(monday.AddDays(-3)) + Empty(monday.AddDays(-2)) + Empty(monday.AddDays(-1));

        Assert.StartsWith("DARK: Campaign emails (8 AM): 0 sent in 3 days.", DarkLanes.Line(Campaign, Reads(pass, tracker: SentOn("11/27/2026")), monday));
        // A pass that held for the day but still sent is a send, not a held day.
        string sentAnyway = pass.Replace("0 sent of 10", "2 sent of 10", StringComparison.Ordinal).Replace("held: 51 follow-up", "held: 1 no_email_day", StringComparison.Ordinal);
        Assert.Null(DarkLanes.Line(Campaign, Reads(sentAnyway, tracker: SentOn("11/27/2026")), monday));
    }

    private static string SentOn(string day) =>
        $"Count,Person,Company,Communication,Date,Phone/Email,Notes\n4100,Ann Lee,Acme,Email,{day},ann@acme.test,\"CAMPAIGN EMAIL SENT 8:00 AM CST\"\n";
}
