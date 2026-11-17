using JobHunt.Functions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The email reconnect's two modes end to end on fakes: shadow never calls the mail port and drafts in RECONNECT-v1
/// beside the would-send HTML; `on` never calls the draft port for an email row, sends into the thread, counts a send
/// only once Sent shows it, and records it (the row, the code's spool row, the tracker fragment, the day's sent file);
/// a guard holds and drafts nothing; a flip of NETWORK_SEND sends the day's drafts and deletes them; a bounce asks
/// Hunter and sends again only to a different address. The 07:30 card lists the day's sends.
/// </summary>
public sealed class ReconnectSendTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("reconnect-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Day = new(2026, 11, 23);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The mail port, recording; a message id in <see cref="NotInSent"/> never shows under Sent.</summary>
    private sealed class Mail : IMailSender, ISentReader
    {
        public List<OutgoingMail> Sent { get; } = [];

        public HashSet<string> NotInSent { get; } = new(StringComparer.Ordinal);

        public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
        {
            Sent.Add(mail);
            return Task.FromResult(new SentMail($"s-{Sent.Count}", mail.ThreadId ?? "new", $"<s{Sent.Count}@gmail.com>"));
        }

        public Task<bool> InSentAsync(string messageId, CancellationToken ct) => Task.FromResult(!NotInSent.Contains(messageId));
    }

    private sealed class Finder(string? address) : IAddressFinder
    {
        public string Name => "hunter";

        public Task<FoundAddress?> FindAsync(string fullName, string domain, CancellationToken ct) =>
            Task.FromResult(address is null ? null : new FoundAddress(address, "https://hunter.io"));
    }

    private static Conversation Due(string key = "pat@acme.com", long id = 1) => new()
    {
        Id = id,
        PersonKey = key,
        FirstName = "Pat",
        FullName = "Pat Lee",
        Company = "Acme",
        CompanyKey = "acme.com",
        Email = key,
        Source = "gmail",
        ThreadIds = ["t-" + id],
        FirstTwoWayAt = Monday.AddDays(-40),
        LastInboundAt = Monday.AddDays(-26),
        LastOutboundAt = Monday.AddDays(-25),
        LastChannel = Channel.Email,
        LastSubject = "Re: Platform role",
        TopicLine = "the platform consolidation on .NET 10",
    };

    private (MondayDrafts Build, NetworkFakes.Rows Rows, NetworkFakes.Drafts Drafts, Mail Mail, NetworkFakes.Gmail Gmail) Build(SendMode mode)
    {
        var rows = new NetworkFakes.Rows();
        var gmail = new NetworkFakes.Gmail();
        foreach (long id in (long[])[1, 2])
        {
            gmail.Threads["t-" + id] = new ThreadSummary("t-" + id, "Platform role", [
                new ThreadMessage("Pat Lee <pat@acme.com>", Monday.AddDays(-26), false, false, "", "stevenstout11@gmail.com", "<a1@acme.com>"),
                new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Monday.AddDays(-25), true, false, "", "pat@acme.com", "<s1@gmail.com>")]);
        }
        var drafts = new NetworkFakes.Drafts();
        var mail = new Mail();
        var share = new FolderMergeStore(_root);
        var send = new Reconnects(rows, gmail, drafts, share, new NetworkFakes.FixedTime(Monday)) { Mode = mode, Mail = mail, SentBox = mail, Spacing = TimeSpan.Zero };
        return (new MondayDrafts(rows, gmail, drafts, share, new NetworkFakes.Blog()) { Reconnect = send }, rows, drafts, mail, gmail);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(_root, path));

    private IEnumerable<string> Files(string folder) =>
        Directory.Exists(Path.Combine(_root, folder)) ? Directory.EnumerateFiles(Path.Combine(_root, folder)).Select(Path.GetFileName)!.OfType<string>() : [];

    [Fact]
    public async Task Shadow_never_calls_the_mail_port_and_drafts_RECONNECT_v1_beside_the_would_send()
    {
        var (build, rows, drafts, mail, _) = Build(SendMode.Shadow);
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);

        Assert.Empty(mail.Sent);
        Assert.Equal((1, 1, 0), (result.Due, result.Drafted, result.Sent));
        var draft = Assert.Single(drafts.Written);
        Assert.StartsWith("Pat,\n\nIt's been a few weeks since we talked about the platform consolidation on .NET 10.\n\nIs it a bad idea to stay in contact?", draft.Text, StringComparison.Ordinal);
        string html = Read("networklog/would-sends/2026-11-23/t-1.html");
        Assert.EndsWith(draft.Html + "\n", html, StringComparison.Ordinal);
        Assert.Contains("t-1.html\tPat Lee\tpat@acme.com\tRe: Platform role", Read("networklog/would-sends/2026-11-23/index.tsv"), StringComparison.Ordinal);
        Assert.Single(Files("inbox/link-tokens"));
        Assert.Empty(Files("inbox/tracker-inbox"));
        Assert.Equal(ConversationState.Drafted, rows.Store["pat@acme.com"].State);
    }

    [Fact]
    public async Task On_never_calls_the_draft_port_for_an_email_row_and_records_the_send()
    {
        var (build, rows, drafts, mail, _) = Build(SendMode.On);
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);

        Assert.Empty(drafts.Written);
        Assert.Equal((1, 1), (result.Sent, result.Drafted));
        var sent = Assert.Single(mail.Sent);
        Assert.Equal(("pat@acme.com", "t-1", "<s1@gmail.com>", "Re: Platform role"), (sent.To, sent.ThreadId, sent.InReplyTo, sent.Subject));
        var row = rows.Store["pat@acme.com"];
        Assert.Equal((ConversationState.Drafted, "sent:s-1", Monday), (row.State, row.DraftId, row.DraftedAt));
        string token = Read("inbox/link-tokens/" + Assert.Single(Files("inbox/link-tokens")));
        Assert.StartsWith("code,person,company,channel,role,thread,sent,lane\r\n", token, StringComparison.Ordinal);
        Assert.EndsWith(",Pat Lee,Acme,Email,,t-1,2026-11-23T12:00:00Z,NetworkLogPass\r\n", token, StringComparison.Ordinal);
        Assert.Contains(token.Split("\r\n")[1][..6], sent.Text, StringComparison.Ordinal);
        Assert.Contains("SENT by NetworkLogPass 6:00 AM CST 11/23: email reconnect RECONNECT-v1", Read("inbox/tracker-inbox/" + Assert.Single(Files("inbox/tracker-inbox"))), StringComparison.Ordinal);
        Assert.StartsWith("06:00\tPat Lee\tAcme\tpat@acme.com\tt-1\ts-1\t", Read("networklog/sent/2026-11-23.tsv"), StringComparison.Ordinal);
        Assert.Empty(Files("networklog/would-sends/2026-11-23"));
    }

    [Fact]
    public async Task A_send_Gmail_does_not_show_under_Sent_counts_as_failed()
    {
        var (build, rows, _, mail, _) = Build(SendMode.On);
        mail.NotInSent.Add("s-1");
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);

        Assert.Equal(0, result.Sent);
        Assert.Equal(["pat@acme.com: not in Sent"], result.Failed);
        Assert.Equal(ConversationState.Open, rows.Store["pat@acme.com"].State);
        Assert.Empty(Files("inbox/link-tokens"));
    }

    [Fact]
    public async Task A_guard_holds_drafts_nothing_and_says_which()
    {
        var (build, rows, drafts, mail, gmail) = Build(SendMode.On);
        gmail.Threads["t-1"] = gmail.Threads["t-1"] with { Messages = [.. gmail.Threads["t-1"].Messages, new ThreadMessage("Pat Lee <pat@acme.com>", Monday.AddDays(-22), false, false)] };
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);

        Assert.Equal(1, result.Held["reply owed"]);
        Assert.Empty(mail.Sent);
        Assert.Empty(drafts.Written);
        Assert.Equal(ConversationState.Open, rows.Store["pat@acme.com"].State);
    }

    [Fact]
    public async Task A_flip_after_the_shadow_build_sends_the_day_s_drafts_and_deletes_them_first()
    {
        var (shadow, rows, drafts, _, _) = Build(SendMode.Shadow);
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);
        await shadow.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal("r-1", rows.Store["pat@acme.com"].DraftId);

        var mail = new Mail();
        var share = new FolderMergeStore(_root);
        var gmail = new NetworkFakes.Gmail();
        gmail.Threads["t-1"] = new ThreadSummary("t-1", "Platform role", [new ThreadMessage("Pat Lee <pat@acme.com>", Monday.AddDays(-26), false, false), new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Monday.AddDays(-25), true, false)]);
        var on = new MondayDrafts(rows, gmail, drafts, share, new NetworkFakes.Blog())
        {
            Reconnect = new Reconnects(rows, gmail, drafts, share, new NetworkFakes.FixedTime(Monday.AddHours(3))) { Mode = SendMode.On, Mail = mail, SentBox = mail, Spacing = TimeSpan.Zero },
        };
        var result = await on.RunAsync(NetworkMode.On, Day, await rows.OpenAsync(CancellationToken.None), Monday.AddHours(3), CancellationToken.None);

        Assert.Equal(["r-1"], drafts.Deleted);
        Assert.Equal(1, result.Sent);
        Assert.Equal("sent:s-1", rows.Store["pat@acme.com"].DraftId);
    }

    [Fact]
    public async Task The_flip_re_runs_the_day_once_only_after_would_sends_and_only_in_on()
    {
        var done = new PassRow(Day, "on", Monday, Monday.AddMinutes(2));
        Assert.True(NetworkLogTimer.SendFlipped(done, SendMode.On, wouldSends: true, flipped: false));
        Assert.False(NetworkLogTimer.SendFlipped(done, SendMode.On, wouldSends: true, flipped: true));
        Assert.False(NetworkLogTimer.SendFlipped(done, SendMode.On, wouldSends: false, flipped: false));
        Assert.False(NetworkLogTimer.SendFlipped(done, SendMode.Shadow, wouldSends: true, flipped: false));
        Assert.False(NetworkLogTimer.SendFlipped(done with { FinishedAt = null }, SendMode.On, wouldSends: true, flipped: false));
        Assert.Equal((SendMode.On, SendMode.Shadow, SendMode.Shadow), (NetworkLogTimer.Send(" ON "), NetworkLogTimer.Send(null), NetworkLogTimer.Send("send")));
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("pat.lee@acme.com", true)]
    [InlineData("pat@acme.com", false)]
    [InlineData(null, false)]
    public async Task A_bounce_is_sent_again_only_to_a_different_address_Hunter_verifies(string? found, bool again)
    {
        var (_, rows, drafts, mail, gmail) = Build(SendMode.On);
        await rows.UpsertAsync(Due() with { State = ConversationState.Drafted, DraftId = "sent:s-0", DraftedAt = Monday.AddDays(-1) }, Monday, CancellationToken.None);
        var send = new Reconnects(rows, gmail, drafts, new FolderMergeStore(_root), new NetworkFakes.FixedTime(Monday)) { Mode = SendMode.On, Mail = mail, SentBox = mail, Spacing = TimeSpan.Zero };

        bool resent = await send.BouncedAsync(rows.Store["pat@acme.com"], new Finder(found), Monday, CancellationToken.None);

        Assert.Equal(again, resent);
        Assert.Equal(again ? 1 : 0, mail.Sent.Count);
        Assert.Equal(again ? ConversationState.Drafted : ConversationState.Closed, rows.Store["pat@acme.com"].State);
        if (again)
        {
            Assert.Equal("pat.lee@acme.com", mail.Sent[0].To);
            Assert.Equal("pat.lee@acme.com", rows.Store["pat@acme.com"].Email);
        }
    }

    [Fact]
    public void The_card_lists_the_day_s_sends_and_reads_the_file_the_pass_writes()
    {
        var inputs = new TodayInputs(new DateTime(2026, 11, 23, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            "", "", "", "", "", null, "", "", [], "")
        {
            Reconnects = "06:00\tPat Lee\tAcme\tpat@acme.com\tt-1\ts-1\tabc123\n06:01\tSam Roe\t\tsam@globex.com\tt-2\ts-2\tdef456\n",
        };
        string card = TodayCard.Render(inputs);
        Assert.Contains("## Reconnects sent (2)\n\nFrom networklog/sent/2026-11-23.tsv", card, StringComparison.Ordinal);
        Assert.Contains("- Pat Lee, Acme, sent 06:00\n- Sam Roe, no company, sent 06:01\n", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Reconnects sent", TodayCard.Render(inputs with { Reconnects = "" }), StringComparison.Ordinal);
        Assert.Equal(NetworkFiles.SentPath(Day), TodayCard.ReconnectsPath(Day));
    }
}
