using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The Monday build in `on`: the draft goes into the row's own thread, to the address the person wrote from, under
/// the label id the port answered, is read back before the row is Drafted, is not written twice in one Monday, and a
/// render that fails a check writes nothing; in shadow the same rows are would-drafts and the port is never called.
/// </summary>
public sealed class NetworkDraftTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-drafts-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Conversation Due(string key, long id, string topic = "the platform consolidation on .NET 10") => new()
    {
        Id = id,
        PersonKey = key,
        FirstName = "Pat",
        FullName = "Pat Lee",
        Email = key,
        Source = "gmail",
        ThreadIds = ["t-" + id, "t-old"],
        FirstTwoWayAt = Monday.AddDays(-40),
        LastInboundAt = Monday.AddDays(-26),
        LastOutboundAt = Monday.AddDays(-25),
        LastChannel = Channel.Email,
        LastSubject = "Re: Platform role",
        TopicLine = topic,
    };

    private (MondayDrafts Build, NetworkFakes.Rows Rows, NetworkFakes.Drafts Drafts) Build()
    {
        var rows = new NetworkFakes.Rows();
        var gmail = new NetworkFakes.Gmail();
        gmail.Threads["t-1"] = new ThreadSummary("t-1", "Platform role", [
            new ThreadMessage("Pat Lee <pat@acme.com>", Monday.AddDays(-26), false, false, "", "stevenstout11@gmail.com", "<a1@acme.com>"),
            new ThreadMessage("Steven Stout <stevenstout11@gmail.com>", Monday.AddDays(-25), true, false, "", "pat@acme.com", "<s1@gmail.com>")]);
        gmail.Threads["t-2"] = new ThreadSummary("t-2", "Coffee", []);
        var drafts = new NetworkFakes.Drafts();
        return (new MondayDrafts(rows, gmail, drafts, new FolderMergeStore(_root)), rows, drafts);
    }

    [Fact]
    public async Task On_writes_the_draft_into_the_row_s_thread_under_the_Ready_label_and_marks_it_Drafted()
    {
        var (build, rows, drafts) = Build();
        await rows.UpsertAsync(Due("pat@acme.com", 1), Monday, CancellationToken.None);
        var open = await rows.OpenAsync(CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), open, Monday, CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Due, result.Drafted, result.RenderFailed));
        var mail = Assert.Single(drafts.Written);
        Assert.Equal("t-1", mail.ThreadId);
        Assert.Equal("pat@acme.com", mail.To);
        Assert.Equal("Re: Platform role", mail.Subject);
        Assert.Equal("<s1@gmail.com>", mail.InReplyTo);
        Assert.Equal(mail.InReplyTo, mail.References);
        Assert.Equal([("m-1", "L:Follow-up/Ready")], drafts.Labelled);
        Assert.Equal(["r-1"], drafts.Asked);
        var row = rows.Store["pat@acme.com"];
        Assert.Equal((ConversationState.Drafted, "r-1", Channel.Email, Monday), (row.State, row.DraftId, row.DraftChannel, row.DraftedAt));
    }

    [Fact]
    public async Task A_second_build_the_same_Monday_drafts_nothing_twice()
    {
        var (build, rows, drafts) = Build();
        await rows.UpsertAsync(Due("pat@acme.com", 1), Monday, CancellationToken.None);
        await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        var again = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday.AddHours(1), CancellationToken.None);
        Assert.Equal(0, again.Due);
        Assert.Single(drafts.Written);
    }

    [Fact]
    public async Task A_failed_render_writes_nothing_and_the_row_stays_Open()
    {
        var (build, rows, drafts) = Build();
        await rows.UpsertAsync(Due("pat@acme.com", 1, topic: "whether it ships? and when"), Monday, CancellationToken.None);
        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal((1, 0, 1), (result.Due, result.Drafted, result.RenderFailed));
        Assert.Equal(["pat@acme.com: question"], result.Failed);
        Assert.Empty(drafts.Written);
        Assert.Equal(ConversationState.Open, rows.Store["pat@acme.com"].State);
    }

    [Fact]
    public async Task A_row_last_heard_on_LinkedIn_is_not_drafted_in_Gmail()
    {
        var (build, rows, drafts) = Build();
        await rows.UpsertAsync(Due("pat@acme.com", 1) with { LastChannel = Channel.LinkedIn }, Monday, CancellationToken.None);
        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal((1, 0), (result.Due, result.Drafted));
        Assert.Empty(drafts.Written);
    }

    [Fact]
    public async Task Shadow_writes_the_same_rows_as_would_drafts_and_calls_the_port_never()
    {
        var rows = new NetworkFakes.Rows();
        await rows.UpsertAsync(Due("pat@acme.com", 1), Monday, CancellationToken.None);
        var build = new MondayDrafts(rows, new NetworkFakes.Gmail(), new NoDraftWriter(), new FolderMergeStore(_root));
        var result = await build.RunAsync(NetworkMode.Shadow, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal((1, 1), (result.Due, result.Drafted));
        Assert.Contains("## pat@acme.com", File.ReadAllText(Path.Combine(_root, "networklog", "would-drafts", "2026-11-09.md")), StringComparison.Ordinal);
        Assert.Equal(ConversationState.Open, rows.Store["pat@acme.com"].State);
    }

    [Fact]
    public async Task A_draft_that_does_not_read_back_leaves_the_row_Open()
    {
        var (build, rows, drafts) = Build();
        drafts.Forget = true;
        await rows.UpsertAsync(Due("pat@acme.com", 1), Monday, CancellationToken.None);
        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal(0, result.Drafted);
        Assert.Equal(["pat@acme.com: draft not read back"], result.Failed);
        Assert.Equal(ConversationState.Open, rows.Store["pat@acme.com"].State);
    }
}
