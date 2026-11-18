namespace TheYard.Tests;

/// <summary>
/// The blog's search and keyword rules (ADR: Every LinkedIn post is also a blog post, the addendum on the read
/// model), each on its own: the cut into words, the widening, the alias that stands only for what it names, the
/// keyword minimums, the weights, and the mockup's six searches over the 15 real posts.
/// </summary>
public sealed class BlogSearchRulesTests
{
    // #region the-cut
    [Fact]
    public void A_word_is_letters_digits_and_hash_in_lower_case_and_a_dot_that_opens_one_is_kept_beside_it()
    {
        var words = BlogText.Words("Moving to .NET 10 and C#, not ASP.NET's items").Select(w => w.Word).ToList();
        Assert.Equal(["moving", "to", ".net", "net", "10", "and", "c#", "not", "asp", "net", "s", "items"], words);
    }

    [Fact]
    public void Two_words_with_one_space_between_are_a_pair_and_anything_else_between_them_is_not()
    {
        var counted = BlogText.Counted("Microsoft Copilot, and Microsoft  Copilot again", BlogText.Body).ToDictionary(w => w.Word, w => w.Mentions);
        Assert.Equal(1, counted["microsoft copilot"]);
        Assert.Equal(2, counted["copilot"]);
        Assert.False(counted.ContainsKey("copilot and") && counted["copilot and"] > 1);
        Assert.False(counted.ContainsKey("microsoft  copilot"));
    }

    [Theory]
    [InlineData("copil", 2)]
    [InlineData("copilot", 2)]
    [InlineData("pilot", 0)]
    [InlineData("microsoft copilot", 1)]
    [InlineData("ms", 1)]
    public void A_term_lands_on_the_start_of_a_word_and_a_phrase_on_the_start_of_a_pair(string term, int mentions)
    {
        var counted = BlogText.Counted("Microsoft Copilot answered in 39 ms; copilots too.", BlogText.Body).Select(w => (w.Word, w.Mentions));
        Assert.Equal(mentions, BlogText.Mentions(counted, term));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(230, 1)]
    [InlineData(231, 2)]
    [InlineData(2300, 10)]
    public void Reading_minutes_are_the_words_over_230_rounded_up_and_at_least_one(int words, int minutes) =>
        Assert.Equal(minutes, BlogText.ReadingMinutes(string.Join(' ', Enumerable.Repeat("word", words))));
    // #endregion the-cut

    // #region widening
    [Fact]
    public void A_short_alias_stands_only_for_what_it_names()
    {
        Assert.Equal(["microsoft"], KeywordRules.Expand("ms"));
        Assert.Equal(["microsoft"], KeywordRules.Expand("msft"));
        Assert.Contains(("ms", "microsoft"), KeywordRules.SynonymRows());
        Assert.DoesNotContain(("ms", "ms"), KeywordRules.SynonymRows());
    }

    [Fact]
    public void A_word_widens_to_itself_its_synonyms_and_the_synonyms_of_the_word_without_its_s()
    {
        Assert.Equal(["copilot", "microsoft copilot", "github copilot"], KeywordRules.Expand("copilot"));
        Assert.Equal(["scams", "scam", "scammer", "scammers", "fraud", "phishing", "fake"], KeywordRules.Expand("scams"));
        Assert.Equal(["dotnet", ".net", "c#", "csharp"], KeywordRules.Expand("dotnet"));
        Assert.Equal(["onion"], KeywordRules.Expand("onion"));
        Assert.Contains(("tests", "gate"), KeywordRules.SynonymRows());
    }

    [Fact]
    public void The_synonym_table_s_rows_widen_every_word_exactly_as_the_rules_do()
    {
        var rows = KeywordRules.SynonymRows().ToLookup(r => r.Word, r => r.Term);
        var words = KeywordRules.Synonyms.Keys.SelectMany(k => new[] { k, k + "s", k + "ss", k[..^1] }).Concat(["agents", "kubernetes", "c#", ".net", "copil"]);
        foreach (string word in words.Distinct())
        {
            Assert.Equal(KeywordRules.Expand(word), rows.Contains(word) ? rows[word].ToList() : [word]);
        }
    }

    [Fact]
    public void A_typed_query_is_cut_the_way_the_text_is_and_a_one_letter_word_is_dropped()
    {
        Assert.Equal([".net", "core"], KeywordRules.QueryWords("  .NET   core "));
        Assert.Equal(["ms", "copilot"], KeywordRules.QueryWords("a MS copilot!"));
        Assert.Empty(KeywordRules.QueryWords(null));
    }
    // #endregion widening

    // #region keywords
    [Fact]
    public void A_keyword_counts_when_the_title_names_it_or_the_text_reaches_its_minimum()
    {
        Assert.Equal([("cqrs", 1)], KeywordRules.KeywordsOf("CQRS for beginners", "", ""));
        Assert.Empty(KeywordRules.KeywordsOf("A post", "", "Claude, and Claude."));
        Assert.Equal([("claude", 3)], KeywordRules.KeywordsOf("A post", "Claude wrote it.", "Claude, and Claude."));
        Assert.Equal([("tailscale", 1)], KeywordRules.KeywordsOf("A post", "", "Over Tailscale."));
    }

    [Fact]
    public void A_keyword_is_a_whole_word_so_sqlite_is_not_sql_server_and_solid_counts_only_in_capitals()
    {
        Assert.Empty(KeywordRules.KeywordsOf("A post", "", "SQLite, SQLite, SQLite and SQLite."));
        Assert.Equal([("sql", 4)], KeywordRules.KeywordsOf("A post", "", "SQL Server, Azure SQL, sql and SQL."));
        Assert.Empty(KeywordRules.KeywordsOf("A post", "", "a solid plan, solid work, solid tests"));
        Assert.Equal([("solid", 3)], KeywordRules.KeywordsOf("A post", "", "SOLID, SOLID and SOLID"));
    }

    [Fact]
    public void A_link_s_words_count_and_its_address_does_not()
    {
        var derived = PostDerivation.Of("A post", "Two lines.", "[the code](https://github.com/x/y/blob/main/z.cs) [and](https://github.com/a) [more](https://github.com/b)");
        Assert.Empty(derived.Keywords);
        Assert.DoesNotContain(derived.Words, w => w.Word == "github" || w.Word == "blob");
        Assert.Contains(derived.Words, w => w.Word == "code" && w.Field == BlogText.Body);
    }

    [Fact]
    public void Each_of_the_15_live_posts_gets_the_mockup_s_keywords_less_the_seven_a_link_address_gave_it()
    {
        var wrong = BlogFixture.Posts
            .Select(entry => (entry.Post.Slug, Want: string.Join(",", entry.Keywords),
                Got: string.Join(",", PostDerivation.Of(entry.Post.Title, entry.Post.Summary, entry.Post.Body).Keywords.Select(k => k.Key))))
            .Where(row => row.Want != row.Got)
            .Select(row => $"{row.Slug}: want {row.Want}, got {row.Got}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
    // #endregion keywords

    // #region ranking
    [Fact]
    public void A_word_scores_where_it_lands_best_and_every_word_has_to_land()
    {
        static int Field(string term, byte field) => (term, field) switch
        {
            ("copilot", BlogText.Title) => 1,
            ("copilot", BlogText.Body) => 9,
            ("azure", BlogText.Summary) => 1,
            _ => 0,
        };
        var score = SearchRules.Score(["copilot", "azure"], ["azure"], ["copilot"], Field);
        Assert.NotNull(score);
        Assert.Equal(SearchRules.TitleWeight + SearchRules.TopicWeight, score.Tenths);
        Assert.Equal(["title", "Microsoft Copilot", "Azure", "summary", "9 mentions in the text"], score.Why);
        Assert.Null(SearchRules.Score(["copilot", "kubernetes"], [], [], Field));
        Assert.Equal(34, SearchRules.Score(["fake"], [], [], (term, field) => term == "fake" && field == BlogText.Body ? 5 : 0)!.Tenths);
        Assert.Equal(34, SearchRules.Score(["fake"], [], [], (term, field) => term == "fake" && field == BlogText.Body ? 50 : 0)!.Tenths);
        Assert.Equal(10, SearchRules.Score(["fake"], [], [], (term, field) => term == "fake" && field == BlogText.Body ? 1 : 0)!.Tenths);
    }

    private const string Ignite = "microsoft-ignite-2026-all-in-on-ai-light-on-everything-else";

    /// <summary>The mockup's six searches and the order its own search gave over its own copy of the posts, read by running its page's code.</summary>
    public static TheoryData<string, string> MockupOrders => new()
    {
        { "copilot", Ignite },
        { "ms", $"{Ignite} read-the-query-behind-the-dial onion-architecture-for-beginners cqrs-for-beginners i-plan-with-claude-fable-and-let-other-sessions-write-the-code lee-markum-told-me-to-blog" },
        { "scams", "scammers-read-the-job-market-too my-resume-came-back-as-a-job solid-in-five-real-files" },
        { "llm", $"{Ignite} ais-instinct-was-to-escalate-mine-was-to-look i-plan-with-claude-fable-and-let-other-sessions-write-the-code read-the-query-behind-the-dial quality-on-both-ends my-linux-server-runs-on-one-ethernet-cable-and-an-hdmi-dummy-plug the-spec-has-to-run my-first-linux-server-is-my-old-gaming-pc onion-architecture-for-beginners cqrs-for-beginners my-resume-came-back-as-a-job" },
        { "dotnet", $"{Ignite} onion-architecture-for-beginners cqrs-for-beginners solid-in-five-real-files my-resume-came-back-as-a-job my-first-linux-server-is-my-old-gaming-pc lee-markum-told-me-to-blog quality-on-both-ends scammers-read-the-job-market-too" },
        { "microsoft copilot", Ignite },
    };

    /// <summary>The posts that match, best first, newest first on a tie, each scored from its words, its topics and its keywords.</summary>
    private static IEnumerable<string> Ranked(string query, Func<BlogFixture.Entry, (IReadOnlyList<Data.BlogWord> Words, IReadOnlyList<string> Keywords)> read) =>
        BlogFixture.Posts
            .Select(entry =>
            {
                var (words, keywords) = read(entry);
                int Mentions(string term, byte field) => BlogText.Mentions(words.Where(w => w.Field == field).Select(w => (w.Word, w.Mentions)), term);
                return (entry.Post, Score: SearchRules.Score(KeywordRules.QueryWords(query), entry.Topics, keywords, Mentions));
            })
            .Where(row => row.Score is not null)
            .OrderByDescending(row => row.Score!.Tenths).ThenByDescending(row => row.Post.PublishedAt).ThenByDescending(row => row.Post.Id)
            .Select(row => row.Post.Slug);

    [Theory]
    [MemberData(nameof(MockupOrders))]
    public void Over_the_mockup_s_own_posts_the_rules_give_its_searches_the_same_posts_in_the_same_order(string query, string slugs)
    {
        // The mockup's copy: its text as the body, its keywords as kept, the title and summary as the post has them.
        var ranked = Ranked(query, entry => (
            [.. BlogText.Counted(entry.Post.Title, BlogText.Title).Concat(BlogText.Counted(entry.Post.Summary, BlogText.Summary)).Concat(BlogText.Counted(entry.MockupText, BlogText.Body))
                .Select(w => new Data.BlogWord(w.Word, w.Field, w.Mentions))],
            entry.MockupKeywords));
        Assert.Equal(slugs.Split(' '), ranked);
    }

    [Theory]
    [MemberData(nameof(MockupOrders))]
    public void Over_the_live_posts_the_searches_differ_from_the_mockup_only_where_the_text_or_a_dropped_keyword_does(string query, string slugs)
    {
        var want = slugs.Split(' ').ToList();
        if (query == "dotnet")
        {
            // The live body of the spec post says "C#" ("from a PowerShell script on my laptop to C# in Azure"), a
            // sentence the mockup's copy of its text does not have, so the live search finds it too, last.
            want.Add("the-spec-has-to-run");
        }
        if (query == "llm")
        {
            // Claude on the cable post is one of the seven pairs a link address gave the mockup: as two mentions in
            // the text it scores 1.6, not a keyword's 5, and moves below the spec post (4) and the first Linux post (3).
            string cable = "my-linux-server-runs-on-one-ethernet-cable-and-an-hdmi-dummy-plug";
            want.Remove(cable);
            want.Insert(want.IndexOf("my-first-linux-server-is-my-old-gaming-pc") + 1, cable);
        }
        var ranked = Ranked(query, entry =>
        {
            var derived = PostDerivation.Of(entry.Post.Title, entry.Post.Summary, entry.Post.Body);
            return (derived.Words, [.. derived.Keywords.Select(k => k.Key)]);
        });
        Assert.Equal(want, ranked);
    }
    // #endregion ranking
}
