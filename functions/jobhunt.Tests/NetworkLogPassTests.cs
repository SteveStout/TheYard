using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The network log's daily pass over hand-written fakes: the first pass reads 120 days and later ones two, the
/// campaign's threads are left alone, a stranger on a personal address is counted and not logged, a stored row moves
/// forward and never back, a stop phrase stops, a stale silence closes, the TopicLine export and the Monday
/// would-drafts land on the share, a LinkedIn spool joins its email row, and a shadow pass never calls the draft port.
/// </summary>
public sealed class NetworkLogPassTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 12, 5, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ThreadSummary Thread(string id, string from, DateTimeOffset theirs, DateTimeOffset? steve, string snippet = "", string subject = "Platform role") =>
        new(id, subject, [new ThreadMessage(from, theirs, false, false, snippet, "stevenstout11@gmail.com", $"<{id}@x>"), .. steve is { } s ? new[] { new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", s, true, false) } : []]);

    private (NetworkLogPass Pass, NetworkFakes.Rows Rows, NetworkFakes.Gmail Gmail, FolderMergeStore Share) Build(DateTimeOffset now, IDraftWriter? drafts = null)
    {
        var rows = new NetworkFakes.Rows();
        var gmail = new NetworkFakes.Gmail();
        var share = new FolderMergeStore(_root);
        return (new NetworkLogPass(rows, gmail, drafts ?? new NoDraftWriter(), share, new NetworkFakes.FixedTime(now)), rows, gmail, share);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(_root, path));

    [Fact]
    public async Task The_first_pass_reads_120_days_and_the_next_two()
    {
        var (pass, rows, gmail, _) = Build(Monday);
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(120, gmail.Searches[^1].NewerThanDays);
        rows.Store["x@acme.com"] = Row("x@acme.com", Monday.AddDays(-3));
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(2, gmail.Searches[^1].NewerThanDays);
    }

    [Fact]
    public async Task A_two_way_thread_becomes_a_row_and_the_campaign_s_and_a_stranger_s_do_not()
    {
        var (pass, rows, gmail, _) = Build(Monday);
        gmail.Threads["t1"] = Thread("t1", "Pat Lee <pat@acme.com>", Monday.AddDays(-10), Monday.AddDays(-9));
        gmail.Threads["t2"] = Thread("t2", "Sam Ortiz <sam@globex.com>", Monday.AddDays(-5), Monday.AddDays(-4));
        gmail.Threads["t3"] = Thread("t3", "Chris Neighbour <chris@gmail.com>", Monday.AddDays(-5), Monday.AddDays(-4));
        gmail.Threads["t4"] = Thread("t4", "Dana Cold <dana@initech.com>", Monday.AddDays(-5), null);
        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string> { "t2" }, CancellationToken.None);

        var pat = Assert.Single(rows.Store.Values);
        Assert.Equal("pat@acme.com", pat.PersonKey);
        Assert.Equal("Pat", pat.FirstName);
        Assert.Equal("pat lee|acme", pat.MatchKey);
        Assert.Equal(Monday.AddDays(-9), pat.FirstTwoWayAt);
        Assert.Equal(ConversationState.Open, pat.State);
        Assert.Contains(" personal=1 ", result.Line, StringComparison.Ordinal);
        Assert.Contains(" threads=3 ", result.Line, StringComparison.Ordinal);
        Assert.EndsWith("conversations started this ISO week: 0", result.Line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stored_row_moves_forward_and_never_back()
    {
        var (pass, rows, gmail, _) = Build(Monday);
        rows.Store["pat@acme.com"] = Row("pat@acme.com", Monday.AddDays(-2)) with { FirstTwoWayAt = Monday.AddDays(-30), ThreadIds = ["t0"] };
        gmail.Threads["t1"] = Thread("t1", "Pat Lee <pat@acme.com>", Monday.AddDays(-20), Monday.AddDays(-19));
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        var pat = rows.Store["pat@acme.com"];
        Assert.Equal(Monday.AddDays(-30), pat.FirstTwoWayAt);
        Assert.Equal(Monday.AddDays(-2), pat.LastOutboundAt);
        Assert.Equal(["t1", "t0"], pat.ThreadIds);
    }

    [Fact]
    public async Task A_stop_phrase_stops_and_a_stale_silence_closes_at_its_first_read()
    {
        var (pass, rows, gmail, _) = Build(Monday);
        gmail.Threads["t1"] = new ThreadSummary("t1", "Role", [
            new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Monday.AddDays(-10), true, false),
            new ThreadMessage("Pat Lee <pat@acme.com>", Monday.AddDays(-9), false, false, "Not interested, thanks")]);
        gmail.Threads["t2"] = Thread("t2", "Sam Ortiz <sam@globex.com>", Monday.AddDays(-80), Monday.AddDays(-79));
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(ConversationState.Stopped, rows.Store["pat@acme.com"].State);
        Assert.Equal("not interested", rows.Store["pat@acme.com"].CloseReason);
        Assert.Equal(ConversationState.Closed, rows.Store["sam@globex.com"].State);
        Assert.Equal("stale", rows.Store["sam@globex.com"].CloseReason);
    }

    [Fact]
    public async Task A_shadow_Monday_with_due_rows_writes_would_drafts_and_never_calls_the_draft_port()
    {
        var (pass, rows, _, _) = Build(Monday, new NoDraftWriter());
        rows.Store["pat@acme.com"] = Row("pat@acme.com", Monday.AddDays(-25)) with { TopicLine = "the platform consolidation on .NET 10" };
        rows.Store["sam@globex.com"] = Row("sam@globex.com", Monday.AddDays(-25));
        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);

        Assert.Contains(" due=1 drafted=1 ", result.Line, StringComparison.Ordinal);
        string would = Read("networklog/would-drafts/2026-11-09.md");
        Assert.Contains("## pat@acme.com", would, StringComparison.Ordinal);
        Assert.Contains("- To: pat@acme.com", would, StringComparison.Ordinal);
        Assert.Contains("It's been a few weeks since we talked about the platform consolidation on .NET 10.", would, StringComparison.Ordinal);
        Assert.DoesNotContain("sam@globex.com", would, StringComparison.Ordinal);
        string topics = Read("networklog/topics-needed.tsv");
        Assert.StartsWith("PersonKey\tChannel\tThreadIds\tFirstName\tCompany\tLastSubject\tTextPath\n", topics, StringComparison.Ordinal);
        Assert.Contains("sam@globex.com\tEmail\tt9\tSam\t", topics, StringComparison.Ordinal);
        Assert.DoesNotContain("pat@acme.com", topics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_weekday_writes_no_would_drafts()
    {
        var (pass, rows, _, _) = Build(Monday.AddDays(1));
        rows.Store["pat@acme.com"] = Row("pat@acme.com", Monday.AddDays(-25)) with { TopicLine = "the platform" };
        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Contains(" due=0 drafted=0 ", result.Line, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "networklog", "would-drafts")));
    }

    [Fact]
    public async Task A_LinkedIn_spool_joins_the_email_row_by_name_and_company_and_moves_to_done()
    {
        var (pass, rows, _, _) = Build(Monday);
        rows.Store["pat@acme.com"] = Row("pat@acme.com", Monday.AddDays(-30)) with { MatchKey = "pat lee|acme" };
        Directory.CreateDirectory(Path.Combine(_root, "networklog", "linkedin-inbox"));
        File.WriteAllText(Path.Combine(_root, "networklog", "linkedin-inbox", "20261109-110000Z-server6am.tsv"),
            LinkedInSpool.Header + "\nPat Lee\tAcme Inc\thttps://www.linkedin.com/in/pat-lee/\tli-1\t2026-11-05T14:00:00Z\t2026-11-04T14:00:00Z\t2026-10-01T14:00:00Z\n\tbad\t\t\t\t\t\n", Encoding.UTF8);
        var result = await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);

        var pat = Assert.Single(rows.Store.Values);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 14, 0, 0, TimeSpan.Zero), pat.LastInboundAt);
        Assert.Equal(Channel.LinkedIn, pat.LastChannel);
        Assert.Equal(Channel.Email, pat.Channel);
        Assert.Equal("https://www.linkedin.com/in/pat-lee/", pat.ProfileUrl);
        Assert.Contains(" linkedin=1 ", result.Line, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, "networklog", "linkedin-inbox", "done", "20261109-110000Z-server6am.tsv")));
        Assert.Contains("line 3:", Read("networklog/linkedin-inbox/refused.log"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_person_s_lone_invite_is_read_for_its_calendar_part()
    {
        var (pass, rows, gmail, _) = Build(Monday);
        gmail.Threads["t1"] = Thread("t1", "Pat Lee <pat@acme.com>", Monday.AddDays(-3), null, subject: "Invitation: Intro call @ Tue Nov 10");
        gmail.Calendar.Add("t1");
        gmail.Threads["t2"] = Thread("t2", "Sam Ortiz <sam@globex.com>", Monday.AddDays(-3), null, subject: "Quick question");
        await pass.RunAsync(NetworkMode.Shadow, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(["t1"], gmail.FullReads);
        Assert.Equal(Monday.AddDays(-3), Assert.Single(rows.Store.Values).FirstTwoWayAt);
    }

    [Fact]
    public async Task On_reads_each_drafted_row_and_shadow_never_does()
    {
        var drafts = new NetworkFakes.Drafts();
        var (pass, rows, gmail, _) = Build(Monday, drafts);
        var drafted = Monday.AddDays(-30);
        rows.Store["pat@acme.com"] = Row("pat@acme.com", drafted.AddDays(-21)) with { State = ConversationState.Drafted, DraftId = "r-1", DraftedAt = drafted, DraftChannel = Channel.Email, ThreadIds = ["t9"] };
        gmail.Threads["t9"] = new ThreadSummary("t9", "Role", [new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", drafted.AddDays(1), true, false)]);
        await pass.RunAsync(NetworkMode.On, new HashSet<string>(), CancellationToken.None);
        Assert.Equal(["r-1"], drafts.Asked);
        Assert.Equal(ConversationState.Quiet, rows.Store["pat@acme.com"].State);
        Assert.Equal(1, rows.Store["pat@acme.com"].FollowUpCount);
    }

    [Theory]
    [InlineData("shadow", NetworkMode.Shadow)]
    [InlineData(" ON ", NetworkMode.On)]
    [InlineData("off", NetworkMode.Off)]
    [InlineData(null, NetworkMode.Off)]
    [InlineData("yes", NetworkMode.Off)]
    public void The_switch_reads_shadow_on_and_everything_else_off(string? value, NetworkMode mode) => Assert.Equal(mode, NetworkLogTimer.Mode(value));

    [Fact]
    public void A_day_runs_once_and_a_crashed_pass_is_run_again()
    {
        var now = Monday;
        Assert.Null(NetworkLogTimer.Held(null, now));
        Assert.Equal("already_ran", NetworkLogTimer.Held(new PassRow(new DateOnly(2026, 11, 9), "shadow", now.AddHours(-5), now.AddHours(-5)), now));
        Assert.Equal("running", NetworkLogTimer.Held(new PassRow(new DateOnly(2026, 11, 9), "shadow", now.AddMinutes(-30), null), now));
        Assert.Null(NetworkLogTimer.Held(new PassRow(new DateOnly(2026, 11, 9), "shadow", now.AddHours(-3), null), now));
        Assert.True(CentralClock.IsNetworkHour(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)));
        Assert.True(CentralClock.IsNetworkHour(new DateTimeOffset(2026, 10, 26, 11, 30, 0, TimeSpan.Zero)));
        Assert.False(CentralClock.IsNetworkHour(new DateTimeOffset(2026, 11, 9, 11, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task The_shadow_writer_refuses_every_call()
    {
        IDraftWriter none = new NoDraftWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.EnsureLabelAsync("Follow-up/Ready", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.CreateDraftAsync(new DraftMail("t", "a@b.com", "s", "t", "h", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.LabelAsync("m", "l", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.DraftExistsAsync("r", CancellationToken.None));
    }

    private static Conversation Row(string key, DateTimeOffset last) => new()
    {
        PersonKey = key,
        FirstName = MatchKey.FirstName(key.Split('@')[0]) is { Length: > 0 } f ? char.ToUpperInvariant(f[0]) + f[1..] : "P",
        FullName = key,
        Email = key,
        Source = "gmail",
        ThreadIds = ["t9"],
        FirstTwoWayAt = last.AddDays(-5),
        LastInboundAt = last.AddDays(-1),
        LastOutboundAt = last,
        LastSubject = "Platform role",
    };
}
