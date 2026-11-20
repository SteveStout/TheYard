using JobHunt.Application.Features.Watch;
using JobHunt.Application.Shared.Mail;
using JobHunt.Domain.Features.Card;
using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// What reaches him (selfheal1120 version 6, the year-end plan's 7c): a failure the ladder could not fix is one NOT
/// WORKING line, the first lines of the card and of TODAY.md, and one mail to his own address a failure a day; the line
/// leaves when its test passes twice. A fake sender and a throwaway folder, never Gmail or the share (RULE 79).
/// </summary>
public sealed class NotWorkingTests
{
    private static readonly DateTimeOffset NineThirteen = new(2026, 11, 20, 15, 13, 0, TimeSpan.Zero);
    private static readonly DateTime Local = new(2026, 11, 20, 9, 13, 0);
    private static readonly WatchResult Pounce = new("W2", "pounce mode after go-live", false, "dry", "POUNCE_RUN");

    private sealed class Mail : IMailSender
    {
        public List<OutgoingMail> Sent { get; } = [];

        public bool Fail { get; set; }

        public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
        {
            if (Fail)
            {
                throw new HttpRequestException("Gmail answered 503");
            }
            Sent.Add(mail);
            return Task.FromResult(new SentMail("m-" + Sent.Count, "t-" + Sent.Count, null));
        }
    }

    [Fact]
    public void The_card_prints_the_open_lines_first_and_nothing_when_there_are_none()
    {
        var inputs = new TodayInputs(Local, null, string.Empty, new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, string.Empty, []);
        string line = "NOT WORKING: W2 pounce mode after go-live since 11/20 08:43. Tried: nothing: outside self-heal (e). Needs you: nothing.";
        string card = TodayCard.Render(inputs with { NotWorking = [line] });
        int at = card.IndexOf("## NOT WORKING (dispatch/NOT-WORKING.md)\n\n- " + line + "\n", StringComparison.Ordinal);
        Assert.True(at > 0 && at < card.IndexOf("## CRITICAL sends", StringComparison.Ordinal));
        Assert.True(at < card.IndexOf("## ", card.IndexOf("# TODAY", StringComparison.Ordinal) + 2, StringComparison.Ordinal) + 1);
        Assert.DoesNotContain("NOT WORKING", TodayCard.Render(inputs), StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_clears_only_once_its_test_has_passed_on_two_ticks_and_a_lane_line_is_never_the_watch_s()
    {
        const string File = "> RULES FIRST\n\n# NOT WORKING\n\nNOT WORKING: W12 Merger since 11/20 07:13. Tried: 2 heals (watch:w12-merger). Needs you: nothing.\nNOT WORKING: leaders since 11/19 06:50. Tried: x. Needs you: nothing.\n";
        var merger = new WatchResult("W12", "Merger", true, "ran 09:12", "card");
        Assert.Single(HealLadder.Cleared(File, [merger], "08:58\tW12\tMerger\tpass\tran\n", Local));
        Assert.Empty(HealLadder.Cleared(File, [merger], "08:58\tW12\tMerger\tFAIL\tx\n09:13\tW12\tMerger\tpass\tran\n", Local));
        Assert.Empty(HealLadder.Cleared(File, [merger with { Pass = false }], "08:58\tW12\tMerger\tpass\tran\n", Local));
        Assert.Empty(HealLadder.Cleared(File, [merger with { Pass = null }], "08:58\tW12\tMerger\tpass\tran\n", Local));
    }

    [Fact]
    public void A_new_line_goes_under_TODAY_md_s_title_once()
    {
        const string Today = "> RULES FIRST: x\n\n# TODAY, Friday 11/20\n\nWritten 07:30 Central.\n";
        string line = "NOT WORKING: W2 pounce since 11/20 08:43. Tried: x. Needs you: nothing.";
        string once = HealLadder.OnTheCard(Today, [line]);
        Assert.Equal("> RULES FIRST: x\n\n# TODAY, Friday 11/20\n\n" + line + "\n\nWritten 07:30 Central.\n", once);
        Assert.Equal(once, HealLadder.OnTheCard(once, [line]));
        Assert.Equal("no title here\n", HealLadder.OnTheCard("no title here\n", [line]));
    }

    [Fact]
    public async Task One_mail_to_his_own_address_a_failure_a_day_and_a_cleared_line_moves_out_with_its_HEALED_line()
    {
        using var s = new Scratch();
        s.Write("TODAY.md", "> RULES FIRST: x\n\n# TODAY, Friday 11/20\n\nWritten 07:30 Central.\n");
        var mail = new Mail();
        var heal = new WatchHeal(s.Store, mail);
        Assert.Equal(TodayMail.To, WatchHeal.Own);

        var first = await heal.RunAsync([Pounce], string.Empty, NineThirteen, default);
        OutgoingMail sent = Assert.Single(mail.Sent);
        Assert.Equal((TodayMail.To, "NOT WORKING: W2 pounce mode after go-live"), (sent.To, sent.Subject));
        Assert.StartsWith(Assert.Single(first.NotWorking), sent.Text, StringComparison.Ordinal);
        Assert.Contains("\n\nNOT WORKING: W2 pounce mode after go-live since 11/20 09:13.", s.Text("TODAY.md"), StringComparison.Ordinal);

        s.Write("dispatch/NOT-WORKING.md", string.Empty);
        s.Write("dispatch/HEALED.md", string.Empty);
        await heal.RunAsync([Pounce with { Number = "dry again" }], string.Empty, NineThirteen.AddMinutes(1), default);
        Assert.Single(mail.Sent);

        s.Write("dispatch/NOT-WORKING.md", "NOT WORKING: W12 Merger since 11/20 07:13. Tried: 2 heals. Needs you: nothing.\nNOT WORKING: leaders since 11/19. Tried: x. Needs you: nothing.\n");
        await heal.RunAsync([new WatchResult("W12", "Merger", true, "ran", "card")], "08:43\tW12\tMerger\tpass\tran\n", NineThirteen, default);
        Assert.Equal("NOT WORKING: leaders since 11/19. Tried: x. Needs you: nothing.\n", s.Text("dispatch/NOT-WORKING.md"));
        Assert.Contains("NOT WORKING: W12 Merger since 11/20 07:13. Tried: 2 heals. Needs you: nothing. Cleared 2026-11-20 09:13 CST.", s.Text("dispatch/NOT-WORKING-cleared.md"), StringComparison.Ordinal);
        Assert.Contains("| W12 Merger | its test passed twice | 5 cleared |", s.Text("dispatch/HEALED.md"), StringComparison.Ordinal);

        mail.Fail = true;
        var failed = await heal.RunAsync([Pounce with { Test = "W16", Name = "no-email day" }], string.Empty, NineThirteen, default);
        Assert.Single(failed.NotWorking);
        Assert.DoesNotContain("W16", s.Text("dispatch/watch/not-working-mailed.tsv"), StringComparison.Ordinal);
    }
}
