using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A Monday's due LinkedIn follow-ups in `on`: the week's queue in the reply queue's format 2, every item under section
/// A with its draft beside it, the rows left Open and no Gmail draft written for them; and the morning card shows the
/// week's queue beside the day's reply queue, each draft in full for Steve's send.
/// </summary>
public sealed class NetworkLinkedInQueueTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-linkedin-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 13, 0, 0, TimeSpan.Zero);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Conversation Due() => new()
    {
        Id = 7,
        PersonKey = "linkedin:patlee",
        FirstName = "Pat",
        FullName = "Pat Lee",
        Company = "Acme",
        Channel = Channel.LinkedIn,
        ProfileUrl = "https://www.linkedin.com/in/patlee/",
        Source = "20261020-120000Z-server6am.tsv",
        ThreadIds = ["2-abc123=="],
        FirstTwoWayAt = Monday.AddDays(-40),
        LastInboundAt = Monday.AddDays(-26),
        LastOutboundAt = Monday.AddDays(-25),
        LastChannel = Channel.LinkedIn,
        TopicLine = "the platform consolidation on .NET 10",
    };

    [Fact]
    public void The_week_s_queue_sits_under_its_Monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), FollowUpQueue.Monday(new DateOnly(2026, 11, 16)));
        Assert.Equal(new DateOnly(2026, 11, 16), FollowUpQueue.Monday(new DateOnly(2026, 11, 22)));
        Assert.Equal(new DateOnly(2026, 11, 9), FollowUpQueue.Monday(new DateOnly(2026, 11, 15)));
        Assert.Equal("networklog/linkedin-queue-2026-11-16.md", FollowUpQueue.Path(new DateOnly(2026, 11, 16)));
    }

    [Fact]
    public async Task On_queues_a_LinkedIn_row_s_follow_up_for_Steve_and_writes_no_Gmail_draft()
    {
        var rows = new NetworkFakes.Rows();
        var drafts = new NetworkFakes.Drafts();
        var build = new MondayDrafts(rows, new NetworkFakes.Gmail(), drafts, new FolderMergeStore(_root), new NetworkFakes.Blog());
        await rows.UpsertAsync(Due(), Monday, CancellationToken.None);

        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 16), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Due, result.Drafted, result.RenderFailed));
        Assert.Empty(drafts.Written);
        string queue = File.ReadAllText(Path.Combine(_root, "networklog/linkedin-queue-2026-11-16.md"));
        var item = Assert.Single(TodayParts.Items(queue));
        Assert.Equal(("A", "Pat Lee", "Acme", "thread: linkedin 2-abc123=="), (item.Section, item.Person, item.Company, item.Reference));
        Assert.Equal("networklog/texts/2026-11-16/2-abc123==.txt", item.Draft);
        Assert.False(item.IsEmail);
        Assert.Contains("profile https://www.linkedin.com/in/patlee/", item.Notes[0], StringComparison.Ordinal);
        string text = File.ReadAllText(Path.Combine(_root, item.Draft));
        Assert.StartsWith("Hi Pat,", text, StringComparison.Ordinal);
        Assert.Contains("the platform consolidation on .NET 10", text, StringComparison.Ordinal);
        Assert.Equal(ConversationState.Open, rows.Store["linkedin:patlee"].State);
    }

    [Fact]
    public async Task The_morning_card_shows_the_week_s_follow_ups_in_full()
    {
        using var scratch = new Scratch();
        scratch.Write("networklog/linkedin-queue-2026-11-16.md", NetworkFiles.LinkedInQueueText(new DateOnly(2026, 11, 16), [Due()]));
        scratch.Write("networklog/texts/2026-11-16/2-abc123==.txt", "Hi Pat,\n\nIt's been a few weeks since we talked about the platform consolidation on .NET 10.\n");
        var source = new TodayCardSource(scratch.Store, new NoHttp(), new NetworkFakes.FixedTime(new DateTimeOffset(2026, 11, 17, 13, 30, 0, TimeSpan.Zero)));

        var inputs = await source.ReadAsync(CancellationToken.None);
        string card = TodayCard.Render(inputs);

        Assert.Contains("**Pat Lee (Acme)**, LinkedIn (thread: linkedin 2-abc123==)", card, StringComparison.Ordinal);
        Assert.Contains("since we talked about the platform consolidation on .NET 10", card, StringComparison.Ordinal);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }
}
