namespace TheYard.Tests;

/// <summary>
/// A post's body drawn to HTML once, on save (ADR: Every LinkedIn post is also a blog post, the addendum on the read
/// model): the same small Markdown src/lib/blogBody.ts read on the page, every piece of the writer's text escaped,
/// and only an http or https address let through as a link. The cases are blogBody.test.ts's, so the move from the
/// page to the save changed where the reading happens and nothing about what it reads.
/// </summary>
public sealed class BlogBodyTests
{
    [Fact]
    public void Plain_text_is_paragraphs_that_keep_their_line_breaks_and_a_hashtag_is_a_span_never_a_link()
    {
        string html = BlogBodyHtml.Render("SOLID is five habits.\nIn The Shed: three classes.\n\n#dotnet #csharp and C# stays a word");
        Assert.Equal(
            "<p>SOLID is five habits.<br>In The Shed: three classes.</p>\n<p><span class=\"tag\">#dotnet</span> <span class=\"tag\">#csharp</span> and C# stays a word</p>\n",
            html);
    }

    [Fact]
    public void A_heading_a_list_and_a_paragraph_are_read_apart_and_a_blank_line_ends_each()
    {
        string html = BlogBodyHtml.Render("## 1. Write the spec first\n\nOne scenario.\n- Say what, not how\n- Make it fail\n\nThen.");
        Assert.Equal("<h3>1. Write the spec first</h3>\n<p>One scenario.</p>\n<ul><li>Say what, not how</li><li>Make it fail</li></ul>\n<p>Then.</p>\n", html);
        Assert.Equal(["heading", "paragraph", "list", "paragraph"], BlogMarkdown.Blocks("## A\n\nB\n- c\n- d\n\nE").Select(b => b.Kind));
        Assert.Equal(["list", "paragraph"], BlogMarkdown.Blocks("- c\nE").Select(b => b.Kind));
    }

    [Fact]
    public void Bold_code_and_a_link_are_read_and_only_an_http_or_https_link_gets_through()
    {
        string html = BlogBodyHtml.Render("the test `Assert.Equal` in [ParityTests.cs](https://github.com/x/y) is **the contract**");
        Assert.Equal(
            "<p>the test <code>Assert.Equal</code> in <a href=\"https://github.com/x/y\" target=\"_blank\" rel=\"noreferrer\">ParityTests.cs</a> is <strong>the contract</strong></p>\n",
            html);
        Assert.Equal("<p>[nope](javascript:alert(1))</p>\n", BlogBodyHtml.Render("[nope](javascript:alert(1))"));
    }

    [Fact]
    public void Nothing_the_writer_types_becomes_markup()
    {
        string html = BlogBodyHtml.Render("<script>alert('x')</script> & \"quotes\" `<b>` [a\"b](https://x.example/?q=\"y\")");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;quotes&quot; <code>&lt;b&gt;</code>", html);
        Assert.Contains("href=\"https://x.example/?q=&quot;y&quot;\"", html);
        Assert.Contains(">a&quot;b</a>", html);
    }

    [Fact]
    public void The_plain_text_is_the_words_a_reader_reads_with_a_link_s_words_and_not_its_address()
    {
        Assert.Equal("Heading\nRead the code here\nitem one\n", BlogMarkdown.Plain("# Heading\n\nRead [the code](https://github.com/x) here\n- item **one**"));
        Assert.Equal("the code and more\n", BlogMarkdown.Plain("[the code](https://github.com/x) and `more`"));
    }

    [Fact]
    public void The_live_posts_read_minutes_are_within_one_of_the_mockup_s()
    {
        var far = BlogFixture.Posts
            .Select(entry => (entry.Post.Slug, entry.MockupMinutes, Minutes: BlogText.ReadingMinutes(BlogMarkdown.Plain(entry.Post.Body))))
            .Where(row => Math.Abs(row.Minutes - row.MockupMinutes) > 1)
            .Select(row => $"{row.Slug}: {row.Minutes} against the mockup's {row.MockupMinutes}")
            .ToList();
        Assert.True(far.Count == 0, string.Join("\n", far));
    }
}
