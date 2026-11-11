using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// One hiring signal beyond postings, from the postings already held: a company that opened three or more engineering
/// seats in seven days, by when a scan first saw each posting, counted as distinct titles a seat rule passes. The hour's
/// pounce tick writes it as one line on the share, the TODAY card carries that line under the funnel's, and the
/// postings table has the index the read needs. Companies and titles are invented.
/// </summary>
public sealed class HiringSignalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 15, 5, 0, TimeSpan.Zero);

    private static readonly (string Company, string Title)[] Seen =
    [
        ("Zephyr Labs", "Senior Software Engineer, Payments"),
        ("Zephyr Labs", "Staff Software Engineer - Web"),
        ("Zephyr Labs", "Engineering Manager, Platform"),
        ("Zephyr Labs", "Staff Software Engineer - Web"),
        ("Zephyr Labs", "Account Executive"),
        ("Quill Systems", "Principal Architect"),
        ("Quill Systems", "Senior .NET Developer"),
        ("Quill Systems", "Sales Engineer"),
        ("Quill Systems", "Product Manager"),
        ("Arbor Health", "Software Engineer I"),
        ("Arbor Health", "Software Engineer II"),
        ("Arbor Health", "Senior Software Engineer"),
        ("Arbor Health", "Staff Software Engineer"),
    ];

    [Fact]
    public void A_company_with_three_engineering_titles_in_the_week_is_a_signal_and_sales_or_a_repeat_title_is_not()
    {
        var surges = HiringSignal.Surges(Seen);
        Assert.Equal([("Arbor Health", 4), ("Zephyr Labs", 3)], surges);
        Assert.Equal(
            "Hiring signal (2026-11-11 09:05 Central): 2 companies opened 3 or more engineering seats in the last 7 days, by when the scan first saw each: Arbor Health (4), Zephyr Labs (3). From jobhunt.Postings.",
            HiringSignal.Line(CentralClock.Local(Now), surges));
        Assert.Equal(
            "Hiring signal (2026-11-11 09:05 Central): no company opened 3 or more engineering seats in the last 7 days. From jobhunt.Postings.",
            HiringSignal.Line(CentralClock.Local(Now), []));
    }

    [Fact]
    public async Task The_pass_reads_the_last_seven_days_and_writes_one_line_the_TODAY_card_carries()
    {
        using var scratch = new Scratch();
        var openings = new Openings(Seen);
        int companies = await new HiringSignals(scratch.Store, openings, new Fixed(Now)).RunAsync(CancellationToken.None);

        Assert.Equal(2, companies);
        Assert.Equal(Now.AddDays(-7), openings.Since);
        string line = scratch.Text(HiringSignal.Path).Trim();
        Assert.StartsWith("Hiring signal (2026-11-11 09:05 Central): 2 companies", line, StringComparison.Ordinal);
        Assert.Equal(HiringSignal.Path, TodayCard.SignalPath);

        var inputs = new TodayInputs(new DateTime(2026, 11, 11, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            "", "", "", "", "", null, "", "", [], "Funnel, last 7 days: 1 call booked.") { Signal = line };
        string card = TodayCard.Render(inputs);
        Assert.True(card.IndexOf("Funnel, last 7 days", StringComparison.Ordinal) < card.IndexOf(line, StringComparison.Ordinal));
        Assert.DoesNotContain("Hiring signal", TodayCard.Render(inputs with { Signal = "" }), StringComparison.Ordinal);
    }

    [Fact]
    public void The_postings_table_has_an_index_on_when_a_posting_was_first_seen()
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "functions", "JobHunt.Database")))
        {
            root = Directory.GetParent(root)!.FullName;
        }
        string table = File.ReadAllText(Path.Combine(root, "functions", "JobHunt.Database", "Tables", "Postings.sql"));
        Assert.Contains("CREATE INDEX [IX_Postings_FirstSeenAt] ON [jobhunt].[Postings] ([FirstSeenAt])", table, StringComparison.Ordinal);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Openings(IReadOnlyList<(string Company, string Title)> seen) : IOpenings
    {
        public DateTimeOffset? Since { get; private set; }

        public Task<IReadOnlyList<(string Company, string Title)>> FirstSeenSinceAsync(DateTimeOffset since, CancellationToken ct)
        {
            Since = since;
            return Task.FromResult(seen);
        }
    }
}
