using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The second touch after a pounce note is opened: the pass reads the site's opens for codes minted on pounce notes, and
/// a code that reached rung 2 or was opened twice queues one dispatcher item for a follow-up on the first touch's channel,
/// 60 words at most with one tagged link to the next rung and one no-oriented question. One follow-up per person per role,
/// none for a person who replied, nothing at all when POUNCE_RUN is off, and a dry run that queues nothing and writes the
/// minutes from each open to its dry follow-up. The opens are real-shaped: the site's own answers, codes invented.
/// </summary>
public sealed partial class PounceFollowUpTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 16, 5, 0, TimeSpan.Zero);

    private const string Tokens = """
        code,person,company,channel,role,thread,sent,lane
        p4k2qa,Dana Whitfield,Zephyr Labs,LinkedIn,,,2026-11-11T14:10:00Z,pounce-12786
        h7m3zt,"Leader at Corvid Health, name found at send",Corvid Health,LinkedIn,,,2026-11-11T13:02:00Z,pounce-12801
        w9d1rc,Omar Haddad,Quill Systems,LinkedIn,,,2026-11-10T22:40:00Z,pounce-12815
        k3v7yn,Lee Park,Arbor Health,LinkedIn,,,2026-11-11T13:30:00Z,pounce-12820
        t2b6mf,Dana Whitfield,Zephyr Labs,LinkedIn,,,2026-11-11T14:20:00Z,pounce-12786
        s1x8vb,Rita Gomez,Old Co,LinkedIn,,,2026-11-11T12:00:00Z,server6am
        g5n2kd,Dana Whitfield,Zephyr Labs,LinkedIn,,,2026-11-11T14:12:00Z,pounce90-12786
        zzpq01,Test Reader,Zephyr Labs,LinkedIn,,,2026-11-11T12:00:00Z,pounce-12786
        """;

    // 2026-11-11 15:41Z is 1794411660000 ms; the site's answers as /api/admin/links and /api/admin/review give them.
    private const string Links = """
        {"available":true,"note":null,"window":"7d","codes":[
        {"code":"w9d1rc","first_ms":1794412680000,"last_ms":1794412800000,"opens":3,"days":1,"paths":{"/":1,"/api/go/github":1,"/resume/linkedin":1}},
        {"code":"p4k2qa","first_ms":1794411660000,"last_ms":1794411840000,"opens":2,"days":1,"paths":{"/":2}},
        {"code":"t2b6mf","first_ms":1794411900000,"last_ms":1794411960000,"opens":2,"days":1,"paths":{"/":2}},
        {"code":"h7m3zt","first_ms":1794410400000,"last_ms":1794411060000,"opens":1,"days":1,"paths":{"/review":1}},
        {"code":"k3v7yn","first_ms":1794411000000,"last_ms":1794411000000,"opens":1,"days":1,"paths":{"/":1}},
        {"code":"s1x8vb","first_ms":1794411000000,"last_ms":1794411300000,"opens":2,"days":1,"paths":{"/":2}}],"keys":["github","linkedin"]}
        """;

    private const string Review = """
        {"available":true,"window":"7d","opened":3,"reached_by_stop":[3,1,1,1,0,0,0,0],"by_code":[
        {"code":"h7m3zt","stop":3,"minutes":4,"last_day":"2026-11-11"},{"code":"p4k2qa","stop":0,"minutes":1,"last_day":"2026-11-11"}],
        "counted_from":"2026-11-04","source":"kept in Azure Cosmos DB beside the site's activity, with no expiry"}
        """;

    // The row the box writes when the invites lane sends a note whose name it found at send: the name it read, and the code.
    private static TrackerTouch Sent(string person, string company, string code) =>
        new(["2", person, company, "LinkedIn", "11/11/2026", "", $"CONNECTION REQUEST SENT by Claude (server6am). Page https://www.linkedin.com/in/maralind. Code {code}. Text: theyard.stevenstout.biz/?r={code}"], Now.AddMinutes(-170));

    private static FollowUp Due(string code, string channel = "LinkedIn")
    {
        PounceCode pounce = Assert.Single(PounceFollowUp.Codes(Tokens), c => c.Code == code) with { Channel = channel };
        CodeVisit visit = Assert.Single(SiteVisits.Parse(Links, Review)!, v => v.Code == code);
        int rung = PounceFollowUp.Rung(visit);
        return new FollowUp(pounce, visit, rung, PounceFollowUp.Text(pounce, rung));
    }

    [Fact]
    public void Only_codes_minted_on_pounce_notes_are_read_and_the_site_s_answers_parse_to_one_visit_a_code()
    {
        var codes = PounceFollowUp.Codes(Tokens);
        Assert.Equal(["p4k2qa", "h7m3zt", "w9d1rc", "k3v7yn", "t2b6mf"], codes.Select(c => c.Code));
        Assert.Equal((12801L, "Corvid Health", "LinkedIn"), (codes[1].Key, codes[1].Company, codes[1].Channel));

        var visits = SiteVisits.Parse(Links, Review)!;
        Assert.Equal(6, visits.Count);
        Assert.Equal((3, 1), (visits.Single(v => v.Code == "h7m3zt").Stop, visits.Single(v => v.Code == "h7m3zt").Opens));
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 15, 41, 0, TimeSpan.Zero), visits.Single(v => v.Code == "p4k2qa").FirstOpen);
        Assert.Null(SiteVisits.Parse("""{"available":false,"note":"not kept","window":"7d","codes":[],"keys":[]}""", null));
        Assert.Equal(0, SiteVisits.Parse(Links, null)!.Single(v => v.Code == "h7m3zt").Stop);
    }

    [Fact]
    public void A_code_earns_its_follow_up_at_rung_2_or_on_a_second_open_and_rung_3_is_the_code_on_GitHub()
    {
        var visits = SiteVisits.Parse(Links, Review)!.ToDictionary(v => v.Code);
        Assert.Equal((1, true), (PounceFollowUp.Rung(visits["p4k2qa"]), PounceFollowUp.Due(visits["p4k2qa"])));
        Assert.Equal((2, true), (PounceFollowUp.Rung(visits["h7m3zt"]), PounceFollowUp.Due(visits["h7m3zt"])));
        Assert.Equal((3, true), (PounceFollowUp.Rung(visits["w9d1rc"]), PounceFollowUp.Due(visits["w9d1rc"])));
        Assert.Equal((1, false), (PounceFollowUp.Rung(visits["k3v7yn"]), PounceFollowUp.Due(visits["k3v7yn"])));
        Assert.Equal(2, PounceFollowUp.Rung(visits["k3v7yn"] with { Paths = new Dictionary<string, int> { ["/about"] = 1 } }));
    }

    [Theory]
    [InlineData("p4k2qa", "Dana, thank you", "theyard.stevenstout.biz/?view=blog&r=p4k2qa")]
    [InlineData("h7m3zt", "Thank you", "theyard.stevenstout.biz/?doc=adr-coderabbit-one-rung-at-a-time&r=h7m3zt")]
    [InlineData("w9d1rc", "Omar, thank you", "theyard.stevenstout.biz/about?r=w9d1rc")]
    public void Each_follow_up_is_60_words_at_most_with_one_tagged_link_to_the_next_rung_and_one_no_oriented_question(string code, string opens, string link)
    {
        string text = Due(code).Text;
        Assert.StartsWith(opens, text, StringComparison.Ordinal);
        Assert.InRange(PounceFollowUp.Words(text), 20, PounceFollowUp.MaxWords);
        Assert.Equal(link, Assert.Single(Regex.Matches(text, @"theyard\.stevenstout\.biz\S*")).Value);
        Assert.Single(Regex.Matches(text, @"\?(\s|$)"));
        Assert.Contains("\nWould it be a bad idea to ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain((char)0x2014, text);
    }

    [Fact]
    public async Task A_pass_queues_one_item_per_person_per_role_and_none_for_a_person_who_replied()
    {
        using var scratch = new Scratch();
        scratch.Write(PounceFollowUp.Tokens, Tokens);
        var tracker = new Tracker { Since = { new TrackerTouch(["1", "Omar Haddad", "Quill Systems", "LinkedIn", "11/11/2026", "", "INBOUND REPLY, read on the page"], Now.AddMinutes(-70)), Sent("Mara Lind", "Corvid Health", "h7m3zt") } };
        var follow = new PounceFollowUps(scratch.Store, new Visits(), tracker, new Fixed(Now));

        var pass = await follow.RunAsync(PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal(new FollowUpPass(5, 5, 4, 2, 1, 1), pass);
        Assert.Equal(["15-pouncefollow-h7m3zt.md", "15-pouncefollow-p4k2qa.md"], Directory.GetFiles(Path.Combine(scratch.Root, "dispatch", "ready")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(scratch.Has("nightly/pounce/followups/12786-danawhitfield.md") && scratch.Has("nightly/pounce/followups/12801-maralind.md") && scratch.Has("nightly/pounce/followups/12815-omarhaddad.md"));
        string log = scratch.Text(PounceFollowUp.Log);
        Assert.Contains("2026-11-11 10:05:00 follow-up: p4k2qa Dana Whitfield, Zephyr Labs (role 12786, LinkedIn) | rung 1, 2 opens, stop 0 | queued dispatch/ready/15-pouncefollow-p4k2qa.md (dry) | 24 min from the first open", log, StringComparison.Ordinal);
        Assert.Contains("w9d1rc Omar Haddad, Quill Systems (role 12815, LinkedIn) | rung 3, 3 opens, stop 0 | replied, no follow-up | 7 min", log, StringComparison.Ordinal);

        var again = await follow.RunAsync(PounceHandoff.Dry, CancellationToken.None);
        Assert.Equal(new FollowUpPass(5, 5, 4, 0, 0, 4), again);
        Assert.Equal(3, scratch.Text(PounceFollowUp.Log).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void A_dry_item_opens_no_browser_a_live_one_sends_only_to_an_accepted_leader_and_email_stays_in_shadow()
    {
        string dry = FollowUpItem.Item(Due("p4k2qa"), PounceHandoff.Dry, Now);
        var header = dry.Split('\n').TakeWhile(line => line.Contains(':', StringComparison.Ordinal) && !line.StartsWith('#')).ToList();
        Assert.Equal(["Lane: pouncefollow-p4k2qa", "Model: claude-sonnet-5-5"], header.Take(2));
        Assert.Contains("Holds:", header);
        Assert.Contains("Needs: jdrive", header);
        Assert.DoesNotContain(header, line => Holding().IsMatch(line));
        Assert.Contains("This is a DRY RUN", dry, StringComparison.Ordinal);
        Assert.Contains("Minutes from open to dry follow-up:", dry, StringComparison.Ordinal);
        Assert.Contains(Due("p4k2qa").Text, dry, StringComparison.Ordinal);

        string live = FollowUpItem.Item(Due("p4k2qa"), PounceHandoff.Live, Now);
        Assert.Contains("Holds: chrome, linkedin\n", live, StringComparison.Ordinal);
        Assert.Contains("1st-degree connection, send nothing", live, StringComparison.Ordinal);
        Assert.Contains("If he has written anything to Steve, send nothing", live, StringComparison.Ordinal);
        Assert.Contains("standing permission of 22 October", live, StringComparison.Ordinal);

        string email = FollowUpItem.Item(Due("p4k2qa", "Email"), PounceHandoff.Live, Now);
        Assert.False(FollowUpItem.Sends(Due("p4k2qa", "Email"), PounceHandoff.Live));
        Assert.Contains("This is SHADOW", email, StringComparison.Ordinal);
        Assert.Contains("Holds:\n", email, StringComparison.Ordinal);
        Assert.All(new[] { dry, live, email }, item => Assert.DoesNotContain("view=admin", item, StringComparison.OrdinalIgnoreCase));
        Assert.All(new[] { dry, live, email }, item => Assert.DoesNotContain((char)0x2014, item));
    }

    [Fact]
    public async Task Off_reads_and_writes_nothing_and_a_site_that_does_not_answer_queues_nothing()
    {
        using var scratch = new Scratch();
        scratch.Write(PounceFollowUp.Tokens, Tokens);
        var visits = new Visits();
        var off = await new PounceFollowUps(scratch.Store, visits, new Tracker(), new Fixed(Now)).RunAsync(PounceHandoff.Off, CancellationToken.None);
        Assert.Equal(("switch_off", 0), (off.Blocked, visits.Reads));

        var down = await new PounceFollowUps(scratch.Store, new Visits { Down = true }, new Tracker(), new Fixed(Now)).RunAsync(PounceHandoff.Live, CancellationToken.None);
        Assert.Equal(("site_not_read", 5, 0), (down.Blocked, down.Codes, down.Queued));
        Assert.False(scratch.Has(PounceFollowUp.Log) || Directory.Exists(Path.Combine(scratch.Root, "dispatch")));

        var run = PounceFollowUpTimer.Counted(new RunLine("PounceFollowUp", Now), down);
        Assert.Equal(("blocked", 5L), (run.Outcome, run.Counts["codes"]));
    }

    [Fact]
    public async Task The_dry_run_given_three_real_shaped_opens_writes_the_minutes_from_each_open_to_its_dry_follow_up()
    {
        using var scratch = new Scratch();
        scratch.Write(PounceFollowUp.Tokens, Tokens);

        var lines = await new PounceFollowUps(scratch.Store, new Visits(), new Tracker { Since = { Sent("Mara Lind", "Corvid Health", "h7m3zt") } }, new Fixed(Now)).DryRunAsync(CancellationToken.None);

        string report = scratch.Text("nightly/pounce/followup-dry/20261111-160500Z.md");
        Assert.Equal(string.Join("\n", lines) + "\n", report);
        Assert.StartsWith("Follow-up dry run 2026-11-11 10:05:00 Central: 5 pounce codes, 5 opened, 4 due.", report, StringComparison.Ordinal);
        Assert.Equal(["45", "24", "7"], Regex.Matches(report, @"Minutes from open to dry follow-up: (\d+)").Select(m => m.Groups[1].Value));
        Assert.Contains("t2b6mf Dana Whitfield, Zephyr Labs (role 12786, LinkedIn) | rung 1, 2 opens, stop 0 | already has a follow-up for this role", report, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(scratch.Root, "dispatch")) || scratch.Has("nightly/pounce/followups/12786-danawhitfield.md"));
    }

    [Fact]
    public async Task A_code_still_in_the_spool_is_read_beside_the_folded_list_and_one_in_both_counts_once()
    {
        using var scratch = new Scratch();
        string[] lines = Tokens.Split('\n');
        scratch.Write(PounceFollowUp.Tokens, lines[0] + "\n" + lines[3] + "\n");
        scratch.Write(PounceFollowUp.Spool + "/20261111-141000Z-pounce-12786-p4k2qa.csv", lines[0] + "\n" + lines[1] + "\n");
        scratch.Write(PounceFollowUp.Spool + "/20261110-224000Z-pounce-12815-w9d1rc.csv", lines[0] + "\n" + lines[3] + "\n");
        scratch.Write(PounceFollowUp.Spool + "/merged/20261109-120000Z-pounce-12820-k3v7yn.csv", lines[0] + "\n" + lines[4] + "\n");

        var pass = await new PounceFollowUps(scratch.Store, new Visits(), new Tracker(), new Fixed(Now)).RunAsync(PounceHandoff.Dry, CancellationToken.None);

        Assert.Equal((2, 2, 2), (pass.Codes, pass.Due, pass.Queued));
    }

    [Fact]
    public async Task Two_codes_for_one_unnamed_recipient_and_role_resolve_to_the_name_the_send_recorded_and_an_unrecorded_one_waits()
    {
        using var scratch = new Scratch();
        scratch.Write(PounceFollowUp.Tokens, """
            code,person,company,channel,role,thread,sent,lane
            h7m3zt,"Leader at Corvid Health, name found at send",Corvid Health,LinkedIn,,,2026-11-11T13:02:00Z,pounce-12801
            j8c4wp,"Leader at Corvid Health, name found at send",Corvid Health,LinkedIn,,,2026-11-11T13:04:00Z,pounce-12801
            q5r1nd,"Leader at Basalt, name found at send",Basalt,LinkedIn,,,2026-11-11T13:06:00Z,pounce-12840
            """);
        var visits = new Visits
        {
            Links = """
                {"available":true,"note":null,"window":"7d","codes":[
                {"code":"h7m3zt","first_ms":1794410400000,"last_ms":1794411060000,"opens":2,"days":1,"paths":{"/":2}},
                {"code":"j8c4wp","first_ms":1794411000000,"last_ms":1794411300000,"opens":2,"days":1,"paths":{"/":2}},
                {"code":"q5r1nd","first_ms":1794411000000,"last_ms":1794411300000,"opens":2,"days":1,"paths":{"/":2}}],"keys":[]}
                """,
        };
        var tracker = new Tracker { Since = { Sent("Mara Lind", "Corvid Health", "h7m3zt"), Sent("Mara Lind", "Corvid Health", "j8c4wp") } };

        var pass = await new PounceFollowUps(scratch.Store, visits, tracker, new Fixed(Now)).RunAsync(PounceHandoff.Live, CancellationToken.None);

        Assert.Equal(new FollowUpPass(3, 3, 3, 1, 0, 1, Unresolved: 1), pass);
        Assert.Equal(["12801-maralind.md"], Directory.GetFiles(Path.Combine(scratch.Root, "nightly", "pounce", "followups")).Select(Path.GetFileName));
        string item = scratch.Text("dispatch/ready/15-pouncefollow-h7m3zt.md");
        Assert.Contains("Mara, thank you", item, StringComparison.Ordinal);
        Assert.Contains("tracker's row for Mara Lind at Corvid Health", item, StringComparison.Ordinal);
        Assert.DoesNotContain("Leader at", item, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(scratch.Root, "dispatch", "ready")));
        Assert.Equal(("unresolved", 1L), ("unresolved", PounceFollowUpTimer.Counted(new RunLine("PounceFollowUp", Now), pass).Counts["unresolved"]));
    }

    private sealed class Visits : ISiteVisits
    {
        public bool Down { get; init; }

        public string Links { get; init; } = PounceFollowUpTests.Links;

        public int Reads { get; private set; }

        public Task<IReadOnlyList<CodeVisit>?> ReadAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Down ? null : SiteVisits.Parse(Links, Review));
        }
    }

    [GeneratedRegex(@"linkedin|connection request|invite|claude in chrome|chrome", RegexOptions.IgnoreCase)]
    private static partial Regex Holding();

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Tracker : ITrackerRows
    {
        public List<TrackerTouch> Since { get; } = [];

        public Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string[]>>([]);

        public Task<IReadOnlyList<TrackerTouch>> SinceAsync(DateTimeOffset since, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackerTouch>>([.. Since.Where(r => r.WrittenAt >= since)]);
    }
}
