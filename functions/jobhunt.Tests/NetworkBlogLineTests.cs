using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The follow-up's optional post line: what the 4 AM lane may hand over, when the lane is asked for it, when the
/// Function names the post, how the template carries it, and that a draft whose line fails a check goes out without it.
/// </summary>
public sealed class NetworkBlogLineTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netlog-blog-").FullName;

    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 13, 0, 0, TimeSpan.Zero);

    private static readonly BlogPost Linux = new("my-first-linux-server-is-my-old-gaming-pc", "My first Linux server is my old gaming PC", Monday.AddDays(-14));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Conversation Row(string key = "pat@acme.com", long id = 1) => new()
    {
        Id = id,
        PersonKey = key,
        FirstName = "Pat",
        FullName = "Pat Lee",
        Email = key,
        Source = "gmail",
        ThreadIds = ["t-1"],
        FirstTwoWayAt = Monday.AddDays(-40),
        LastInboundAt = Monday.AddDays(-26),
        LastOutboundAt = Monday.AddDays(-25),
        LastChannel = Channel.Email,
        LastSubject = "Re: Platform role",
        TopicLine = "the platform consolidation on .NET 10",
    };

    [Theory]
    [InlineData("the-spec-has-to-run", "abc123", null)]
    [InlineData("The-Spec", "abc123", "slug")]
    [InlineData("the spec", "abc123", "slug")]
    [InlineData("", "abc123", "slug")]
    [InlineData("the-spec-has-to-run", "ABC123", "code")]
    [InlineData("the-spec-has-to-run", "abc12", "code")]
    [InlineData("the-spec-has-to-run", null, "code")]
    public void A_slug_and_a_code_are_the_feed_s_and_make_ref_s_shapes(string slug, string? code, string? fails) =>
        Assert.Equal(fails, BlogLine.Fails(slug, code));

    [Fact]
    public void The_link_is_the_post_s_address_with_the_row_s_code() =>
        Assert.Equal("https://theyard.stevenstout.biz/?view=blog&post=the-spec-has-to-run&r=abc123", BlogLine.Link("the-spec-has-to-run", "abc123"));

    [Theory]
    [InlineData(17, false)]
    [InlineData(18, true)]
    [InlineData(25, true)]
    [InlineData(45, true)]
    [InlineData(46, false)]
    public void The_lane_is_asked_for_a_post_from_three_days_before_due_until_stale(int daysSilent, bool needed) =>
        Assert.Equal(needed, BlogLine.Needed(Row() with { LastInboundAt = Monday.AddDays(-daysSilent - 1), LastOutboundAt = Monday.AddDays(-daysSilent) }, Monday));

    [Fact]
    public void A_row_with_a_post_already_or_last_heard_on_LinkedIn_is_not_asked()
    {
        Assert.False(BlogLine.Needed(Row() with { BlogSlug = "x", RefCode = "abc123" }, Monday));
        Assert.False(BlogLine.Needed(Row() with { LastChannel = Channel.LinkedIn }, Monday));
        Assert.False(BlogLine.Needed(Row() with { State = ConversationState.Drafted }, Monday));
    }

    [Fact]
    public void The_post_is_named_only_when_the_row_holds_it_and_it_went_out_after_Steve_last_wrote()
    {
        var row = Row() with { BlogSlug = Linux.Slug, RefCode = "abc123" };
        Assert.Equal(new BlogMention(Linux.Title, BlogLine.Link(Linux.Slug, "abc123")), BlogLine.For(row, [Linux]));
        Assert.Null(BlogLine.For(row, []));
        Assert.Null(BlogLine.For(row, [Linux with { PublishedAt = Monday.AddDays(-30) }]));
        Assert.Null(BlogLine.For(row with { RefCode = null }, [Linux]));
    }

    [Fact]
    public void Without_a_post_the_template_renders_as_it_always_has()
    {
        var draft = DraftTemplate.Render("Pat", "the platform consolidation on .NET 10", "Platform role");
        Assert.Equal("Hi Pat,\n\nIt's been a few weeks since we talked about the platform consolidation on .NET 10.\n\nWould it be a bad idea to catch up for 15 minutes in the next couple of weeks?\n\nSteve", draft.Text);
    }

    [Fact]
    public void With_a_post_the_line_follows_the_topic_and_its_link_is_an_anchor_that_is_not_a_question()
    {
        var post = new BlogMention(Linux.Title, BlogLine.Link(Linux.Slug, "abc123"));
        var draft = DraftTemplate.Render("Pat", "the platform consolidation on .NET 10", "Platform role", post);
        Assert.Contains("about the platform consolidation on .NET 10. Since then I wrote up My first Linux server is my old gaming PC: https://theyard.stevenstout.biz/?view=blog&post=my-first-linux-server-is-my-old-gaming-pc&r=abc123\n\nWould", draft.Text, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://theyard.stevenstout.biz/?view=blog&amp;post=my-first-linux-server-is-my-old-gaming-pc&amp;r=abc123\">", draft.Html, StringComparison.Ordinal);
        Assert.Empty(DraftTemplate.Check(draft));
    }

    [Fact]
    public void The_lane_s_file_may_carry_a_post_and_a_code_with_or_without_a_clause()
    {
        var rows = TopicLineRule.Parse("pat@acme.com\tthe platform consolidation on .NET 10\tthe-spec-has-to-run\tabc123\nsam@globex.com\t\tthe-spec-has-to-run\tdef456\nlee@initech.com\t\n", "f.tsv");
        Assert.Equal(["pat@acme.com", "sam@globex.com"], rows.Select(r => r.PersonKey));
        Assert.Equal(("the-spec-has-to-run", "abc123"), (rows[0].BlogSlug, rows[0].RefCode));
        Assert.Equal("", rows[1].TopicLine);
    }

    private void Lane(string day, string text)
    {
        string folder = Path.Combine(_root, "nightly", day, "0400-reply-opus-5-5");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, TopicImport.FileName), text);
    }

    [Fact]
    public async Task The_import_stores_the_first_good_post_once_and_refuses_a_bad_code()
    {
        var rows = new NetworkFakes.Rows();
        await rows.UpsertAsync(Row(), Monday, CancellationToken.None);
        await rows.UpsertAsync(Row("sam@globex.com", 2), Monday, CancellationToken.None);
        Lane("2026-11-06", "pat@acme.com\t\tthe-spec-has-to-run\tabc123\nsam@globex.com\t\tthe-spec-has-to-run\tBAD\n");
        Lane("2026-11-07", "pat@acme.com\t\tsolid-in-five-real-files\tzzz999\n");
        var import = new TopicImport(rows, new FolderMergeStore(_root));

        var result = await import.RunAsync(new DateOnly(2026, 11, 7), CancellationToken.None);
        Assert.Equal((0, 1), (result.Applied, result.Posts));
        Assert.Equal(("the-spec-has-to-run", "abc123"), (rows.Store["pat@acme.com"].BlogSlug, rows.Store["pat@acme.com"].RefCode));
        Assert.Equal("the platform consolidation on .NET 10", rows.Store["pat@acme.com"].TopicLine);
        Assert.Equal(["sam@globex.com: code"], result.Refused);

        Assert.Equal(0, (await import.RunAsync(new DateOnly(2026, 11, 7), CancellationToken.None)).Posts);
    }

    private (MondayDrafts Build, NetworkFakes.Rows Rows, NetworkFakes.Blog Blog) Build(params BlogPost[] posts)
    {
        var gmail = new NetworkFakes.Gmail();
        gmail.Threads["t-1"] = new ThreadSummary("t-1", "Platform role", []);
        var rows = new NetworkFakes.Rows();
        var blog = new NetworkFakes.Blog(posts);
        return (new MondayDrafts(rows, gmail, new NetworkFakes.Drafts(), new FolderMergeStore(_root), blog), rows, blog);
    }

    [Fact]
    public async Task A_would_draft_names_the_post_and_counts_it()
    {
        var (build, rows, _) = Build(Linux);
        await rows.UpsertAsync(Row() with { BlogSlug = Linux.Slug, RefCode = "abc123" }, Monday, CancellationToken.None);
        var result = await build.RunAsync(NetworkMode.Shadow, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal((1, 1, 1), (result.Due, result.Drafted, result.Posts));
        Assert.Contains("Since then I wrote up My first Linux server is my old gaming PC: https://theyard.stevenstout.biz/?view=blog&post=", File.ReadAllText(Path.Combine(_root, "networklog", "would-drafts", "2026-11-09.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_post_line_that_fails_a_check_is_dropped_and_the_draft_still_goes()
    {
        var (build, rows, _) = Build(Linux with { Title = "Why would a server need a monitor?" });
        await rows.UpsertAsync(Row() with { BlogSlug = Linux.Slug, RefCode = "abc123" }, Monday, CancellationToken.None);
        var result = await build.RunAsync(NetworkMode.On, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal((1, 1, 0, 0), (result.Due, result.Drafted, result.RenderFailed, result.Posts));
    }

    [Fact]
    public async Task The_feed_is_not_read_when_no_due_row_names_a_post()
    {
        var (build, rows, blog) = Build(Linux);
        await rows.UpsertAsync(Row(), Monday, CancellationToken.None);
        await build.RunAsync(NetworkMode.Shadow, new DateOnly(2026, 11, 9), await rows.OpenAsync(CancellationToken.None), Monday, CancellationToken.None);
        Assert.Equal(0, blog.Reads);
    }

    [Fact]
    public void The_feed_s_JSON_reads_as_posts_and_skips_an_entry_with_no_date()
    {
        var posts = BlogFeed.Parse("""{"posts":[{"slug":"the-spec-has-to-run","title":"The spec has to run","published_at":"2026-10-22T17:43:00+00:00"},{"slug":"draft","title":"Draft"}]}""");
        var post = Assert.Single(posts);
        Assert.Equal(new BlogPost("the-spec-has-to-run", "The spec has to run", new DateTimeOffset(2026, 10, 22, 17, 43, 0, TimeSpan.Zero)), post);
        Assert.Empty(BlogFeed.Parse("""{"other":[]}"""));
    }

    [Fact]
    public void The_export_says_what_each_row_needs_its_due_day_and_when_Steve_last_wrote()
    {
        string text = NetworkFiles.TopicsNeededText([Row(), Row("sam@globex.com", 2) with { TopicLine = null, LastInboundAt = Monday.AddDays(-6), LastOutboundAt = Monday.AddDays(-5) }], Monday);
        string[] lines = text.TrimEnd('\n').Split('\n');
        Assert.Equal("PersonKey\tChannel\tThreadIds\tFirstName\tCompany\tLastSubject\tTextPath\tNeeds\tDueOn\tLastOutbound", lines[0]);
        Assert.EndsWith("\tpost\t2026-11-05\t2026-10-15T13:00:00.0000000+00:00", lines[1], StringComparison.Ordinal);
        Assert.Contains("\ttopic\t2026-11-25\t", lines[2], StringComparison.Ordinal);
    }
}
