using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The blog's read model as the SQL project declares it (ADR: Every LinkedIn post is also a blog post, the
/// addendum on the read model). SchemaConformanceTests holds the mapping to the tables; these hold what the
/// mapping cannot see: that each index the list, the counts and the search read through is filtered to the
/// rows they read and carries the columns they need, so the query is a seek on a small index and never a
/// scan of the logs. Read from the .sql files, with no connection, like the conformance tests.
/// </summary>
public sealed class BlogSchemaTests
{
    private static string Sql(string relative) =>
        Regex.Replace(File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Database", relative)), @"\s+", " ");

    /// <summary>The one CREATE INDEX statement with this name, whitespace folded, so a test reads it as one line.</summary>
    private static string Index(string relative, string name)
    {
        var match = Regex.Match(Sql(relative), $@"CREATE (UNIQUE )?(CLUSTERED )?INDEX \[{name}\][^;]*;", RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"{relative} declares no index {name}");
        return match.Value;
    }

    // #region blog-indexes
    [Fact]
    public void The_list_reads_published_posts_newest_first_from_one_filtered_covering_index()
    {
        string index = Index("Tables/Blog/Post.sql", "IX_Post_Published");
        Assert.Contains("([PublishedAt] DESC, [Id] DESC)", index);
        Assert.Contains("INCLUDE ([Slug], [Title], [Summary], [ReadingMinutes])", index);
        Assert.Contains("WHERE [IsPublished] = 1", index);
        Assert.DoesNotContain("IX_Post_PublishedAt", Sql("Tables/Blog/Post.sql"));
    }

    [Fact]
    public void A_card_s_reads_are_counted_through_an_index_that_holds_only_people()
    {
        string reads = Index("Tables/Blog/PostView.sql", "IX_PostView_Reads");
        Assert.Contains("([PostId], [ViewedAt])", reads);
        Assert.Contains("INCLUDE ([SessionHash], [RefCode], [Source])", reads);
        Assert.Contains("WHERE [IsBot] = 0", reads);

        string window = Index("Tables/Blog/PostView.sql", "IX_PostView_Window");
        Assert.Contains("([ViewedAt])", window);
        Assert.Contains("WHERE [IsBot] = 0", window);
        Assert.DoesNotContain("IX_PostView_PostId_ViewedAt", Sql("Tables/Blog/PostView.sql"));
    }

    [Fact]
    public void The_funnel_reads_a_window_of_events_through_an_index_that_holds_only_people()
    {
        string events = Index("Tables/Blog/PostEvent.sql", "IX_PostEvent_Window");
        Assert.Contains("([At])", events);
        Assert.Contains("INCLUDE ([PostId], [Type], [SessionHash], [Value])", events);
        Assert.Contains("WHERE [IsBot] = 0", events);
        Assert.DoesNotContain("IX_PostEvent_PostId_Type", Sql("Tables/Blog/PostEvent.sql"));
    }

    [Fact]
    public void A_search_term_is_a_seek_on_the_word_and_a_keyword_chip_a_seek_on_the_keyword()
    {
        string words = Sql("Tables/Blog/PostWord.sql");
        Assert.Contains("CONSTRAINT [PK_PostWord] PRIMARY KEY ([Word], [PostId], [Field])", words);
        Assert.Contains("ON DELETE CASCADE", words);
        Assert.Contains("ON [blog].[PostWord] ([PostId])", Index("Tables/Blog/PostWord.sql", "IX_PostWord_PostId"));

        string keywords = Sql("Tables/Blog/PostKeyword.sql");
        Assert.Contains("CONSTRAINT [PK_PostKeyword] PRIMARY KEY ([PostId], [Keyword])", keywords);
        Assert.Contains("([Keyword], [PostId])", Index("Tables/Blog/PostKeyword.sql", "IX_PostKeyword_Keyword"));

        Assert.Contains("CONSTRAINT [PK_SearchSynonym] PRIMARY KEY ([Word], [Term])", Sql("Tables/Blog/SearchSynonym.sql"));
    }
    // #endregion blog-indexes

    [Fact]
    public void A_post_keeps_its_body_drawn_once_and_the_rules_it_was_derived_with()
    {
        string post = Sql("Tables/Blog/Post.sql");
        Assert.Contains("[BodyHtml] nvarchar(max) NULL", post);
        Assert.Contains("[ReadingMinutes] int NULL", post);
        Assert.Contains("[DerivedWith] int NULL", post);
    }
}
