using System.Net;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The daily card (functions/JobHunt.Infrastructure/Features/Card and JobHunt.Domain's DailyCard): the blockers read off the newest APPLY-BLOCKERS file, the
/// Sheet's stop and the RULE 50 line under "Waiting on you", a fact that could not be read
/// said in words, and the card rewritten only when what it says changed, or at 6 AM. Made-up rows.
/// </summary>
public sealed class DailyCardTests
{
    private const string BlockerFile = "# APPLY-BLOCKERS\n\n## Forms filled\n4. **Acme, Senior Engineer** (Lever 1). Tick the box, click **Submit**. Count 10.\n5. **Globex, Lead** remote. Click Submit.\n";

    [Fact]
    public void The_blockers_are_the_numbered_lines_bold_name_and_first_sentence()
    {
        var blockers = DailyCard.Blockers("APPLY-BLOCKERS-2026-10-21.md", BlockerFile);
        Assert.Equal(["Acme, Senior Engineer: (Lever 1) (APPLY-BLOCKERS-2026-10-21.md)", "Globex, Lead: remote (APPLY-BLOCKERS-2026-10-21.md)"], blockers);
    }

    [Fact]
    public void Rule_50_is_one_line_with_the_count_and_the_five_that_open_soonest()
    {
        var held = Enumerable.Range(1, 7)
            .Select(i => new CapVerdict($"Company {i}", false, new DateOnly(2026, 11, 10 - i), "held"))
            .ToList();
        string line = DailyCardSource.CapLine(held);
        Assert.StartsWith("7 companies are held by RULE 50", line, StringComparison.Ordinal);
        Assert.Contains("the next to open: Company 7 on 2026-11-03; Company 6 on 2026-11-04; Company 5 on 2026-11-05; Company 4 on 2026-11-06; Company 3 on 2026-11-07 (", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Company 2", line, StringComparison.Ordinal);
        Assert.StartsWith("1 company is held", DailyCardSource.CapLine(held.Take(1).ToList()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-22", "APPLY-BLOCKERS-2026-10-21.md")]
    [InlineData("2026-10-26", null)]
    public void The_newest_blocker_file_counts_for_three_days(string today, string? newest) =>
        Assert.Equal(newest, DailyCard.Newest(["APPLY-BLOCKERS-2026-10-19.md", "APPLY-BLOCKERS-2026-10-21.md", "notes.md"], DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task The_card_says_what_waits_and_what_could_not_be_read()
    {
        using var scratch = Folder();
        string card = DailyCard.Render(await Source(scratch, HttpStatusCode.ServiceUnavailable).ReadAsync(CancellationToken.None));
        Assert.Contains("# CARD, Thursday 2026-10-22 14:30 Central", card, StringComparison.Ordinal);
        Assert.Contains("- Acme, Senior Engineer: (Lever 1) (APPLY-BLOCKERS-2026-10-21.md)", card, StringComparison.Ordinal);
        Assert.Contains("- The Sheet waits: at 3: Count 3 is banned", card, StringComparison.Ordinal);
        Assert.Contains("- Tracker: 2 rows, last Count 2", card, StringComparison.Ordinal);
        Assert.Contains("- Merger: last line 30 min ago: MERGED x.csv", card, StringComparison.Ordinal);
        Assert.Contains("CI on main: not read (HttpRequestException)", card, StringComparison.Ordinal);
        Assert.Contains("Interviews: not read", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_cards_that_differ_only_in_their_ages_and_title_read_as_the_same()
    {
        string at10 = "# CARD, Sunday 2026-10-25 10:05 Central\r\n\r\n- merge.log: last line 7 h ago: MERGED x.csv\r\n- sweep.log: last line 12 min ago: ok\r\n- tracker: 3 days ago\r\n";
        string at11 = "# CARD, Sunday 2026-10-25 11:05 Central\r\n\r\n- merge.log: last line 8 h ago: MERGED x.csv\r\n- sweep.log: last line 72 min ago: ok\r\n- tracker: 3 days ago\r\n";
        Assert.Equal(DailyCard.Body(at10), DailyCard.Body(at11));
        Assert.NotEqual(DailyCard.Body(at10), DailyCard.Body(at11.Replace("MERGED x.csv", "MERGED y.csv", StringComparison.Ordinal)));
        Assert.Contains("7 h ago", at10, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_card_is_rewritten_only_when_it_changed()
    {
        using var scratch = Folder();
        var source = Source(scratch, HttpStatusCode.ServiceUnavailable);
        var time = new FixedTime(Afternoon);
        Assert.Equal("written", await DailyCardTimer.WriteAsync(scratch.Store, source, time, CancellationToken.None));
        Assert.Equal("unchanged", await DailyCardTimer.WriteAsync(scratch.Store, source, time, CancellationToken.None));
        // An hour later every age on the card has moved and nothing else has: still unchanged.
        Assert.Equal("unchanged", await DailyCardTimer.WriteAsync(scratch.Store, source, new FixedTime(Afternoon.AddHours(1)), CancellationToken.None));
        scratch.Write("tracker-inbox/merge.log", "2026-10-22 14:00:00 MERGED x.csv\r\n2026-10-22 14:20:00 MERGED y.csv\r\n");
        Assert.Equal("written", await DailyCardTimer.WriteAsync(scratch.Store, source, time, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(scratch.Root, "nightly", "CARD.md")));
    }

    [Fact]
    public void A_call_with_no_way_to_join_is_a_stop_and_a_link_or_number_is_the_how()
    {
        using var events = System.Text.Json.JsonDocument.Parse("""
            {"items":[
              {"summary":"Interview with Acme","start":{"dateTime":"2026-10-23T15:00:00Z"},"hangoutLink":"https://meet.google.com/abc-defg-hij"},
              {"summary":"Recruiter screen, Globex","start":{"dateTime":"2026-10-23T18:00:00Z"},"description":"Join: https://acme.zoom.us/j/123?pwd=x"},
              {"summary":"Phone call with Initech","start":{"dateTime":"2026-10-27T14:00:00Z"},"location":"(314) 555-0100"},
              {"summary":"Technical interview, Umbrella","start":{"dateTime":"2026-10-28T16:00:00Z"}},
              {"summary":"Dentist","start":{"dateTime":"2026-10-24T14:00:00Z"}},
              {"summary":"Interview prep","start":{"date":"2026-10-25"}}
            ]}
            """);
        var lines = CalendarWeek.Lines(events.RootElement);
        Assert.Equal(4, lines.Count);
        Assert.Equal("Fri 10/23 10:00 AM Central, Interview with Acme: Google Meet https://meet.google.com/abc-defg-hij", lines[0]);
        Assert.EndsWith("https://acme.zoom.us/j/123?pwd=x", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("phone (314) 555-0100", lines[2], StringComparison.Ordinal);
        Assert.Equal("STOP: Wed 10/28 11:00 AM Central, Technical interview, Umbrella: no join link or number on the event", lines[3]);
    }

    private static readonly DateTimeOffset Afternoon = new(2026, 10, 22, 19, 30, 0, TimeSpan.Zero);

    private static Scratch Folder()
    {
        var scratch = new Scratch();
        scratch.Master("1,Ann,Acme,Email,10/21/2026,,Application submitted\r\n2,Bo,Globex,Email,10/21/2026,,Application submitted\r\n");
        scratch.Write("APPLY-BLOCKERS-2026-10-21.md", BlockerFile);
        scratch.Write("tracker-inbox/merge.log", "2026-10-22 14:00:00 MERGED x.csv\r\n");
        scratch.Write("sheet-mirror/mirror.log", "2026-10-22 14:22:00 SHADOW sheet 2 master 3 add 0 STOPPED at 3: Count 3 is banned\r\n");
        return scratch;
    }

    private static DailyCardSource Source(Scratch scratch, HttpStatusCode status) =>
        new(scratch.Store, new Factory(status), new FixedTime(Afternoon));

    private sealed class Factory(HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Answer(status));
    }

    private sealed class Answer(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
