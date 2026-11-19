using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// RULE 76 check 8 (Steve 17:22 CST 11/18, "You have to be driving communication to me"): the high-value roles only he
/// can unblock lead the card and the mail's subject counts them; an EMPTY RUN is the system's to diagnose and leaves
/// the card (RULE 77). The jobs file is the 11/18 run's own READY and NEEDS STEVE tables.
/// </summary>
public sealed class TodayUnblockTests
{
    private static readonly string Jobs = File.ReadAllText(ReadyRowFixtures.Path("jobs-2026-11-18.md"));

    private static readonly string[] BeemoSubmitted = ["3672", "NA", "Beemo Automation", "Google Form", "11/16/2026", "", "APPLICATION SUBMITTED by Claude, Principal Engineer (Azure / AI), forms.gle."];

    private static TodayInputs Inputs(IReadOnlyList<UnblockRow> unblock) =>
        new(new DateTime(2026, 11, 19, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            "", "", "", "", "", null, "", "", [], "Funnel, last 7 days: 1 call booked.")
        {
            Signal = "Hiring signal: 101 companies.",
            Unblock = unblock,
        };

    [Fact]
    public void The_1Password_swaps_print_and_Beemo_already_submitted_and_the_live_process_do_not()
    {
        var rows = TodayUnblock.Rows(Jobs, [BeemoSubmitted]);
        Assert.Contains(rows, r => r.Role == "VP, Engineering" && r.BandTop == 483_000 && r.Control.StartsWith("RULE 50 swap", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Role == "Senior Engineering Manager, App Excellence" && r.BandTop == 334_000);
        Assert.DoesNotContain(rows, r => r.Company == "Beemo Automation");
        Assert.DoesNotContain(rows, r => r.Company == "Work Truck Solutions");
        Assert.Equal("VP, Engineering", rows[0].Role);
    }

    [Fact]
    public void Beemo_prints_while_the_tracker_has_no_application_and_a_band_under_200000_never_does()
    {
        var rows = TodayUnblock.Rows(Jobs, []);
        Assert.Contains(rows, r => r.Company == "Beemo Automation" && r.BandTop == 220_000);
        Assert.All(rows, r => Assert.True(r.BandTop >= TodayUnblock.HighValue));
        Assert.True(rows.Count <= TodayUnblock.Most);
    }

    [Theory]
    [InlineData("$302,000-$483,000 USD base", 483_000)]
    [InlineData("$180-220K plus bonus", 220_000)]
    [InlineData("$139,000-$202,000", 202_000)]
    [InlineData("no band published", 0)]
    public void The_band_top_is_read_as_written(string cell, int top) => Assert.Equal(top, TodayUnblock.BandTop(cell));

    [Fact]
    public void The_rows_are_the_card_s_first_section_and_the_subject_tells_the_count()
    {
        var rows = TodayUnblock.Rows(Jobs, [BeemoSubmitted]);
        var inputs = Inputs(rows);
        string card = TodayCard.Render(inputs);
        int first = card.IndexOf("## ", StringComparison.Ordinal);
        Assert.Equal(first, card.IndexOf(TodayUnblock.Heading, StringComparison.Ordinal));
        Assert.Contains("- 1Password, VP, Engineering, band top $483,000: RULE 50 swap", card, StringComparison.Ordinal);
        Assert.Equal($"TODAY, Thu 11/19, {rows.Count} high-value roles need one click each", TodayCard.Subject(inputs));
    }

    [Fact]
    public void Nothing_waiting_prints_no_section_and_the_subject_stays_as_it_was()
    {
        var inputs = Inputs([]);
        Assert.DoesNotContain(TodayUnblock.Heading, TodayCard.Render(inputs), StringComparison.Ordinal);
        Assert.Equal("TODAY, Thu 11/19", TodayCard.Subject(inputs));
    }

    [Fact]
    public void An_empty_run_never_reaches_the_card()
    {
        Assert.DoesNotContain("EMPTY RUN", TodayCard.Render(Inputs([])), StringComparison.Ordinal);
    }
}
