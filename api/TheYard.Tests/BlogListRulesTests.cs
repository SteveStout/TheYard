namespace TheYard.Tests;

/// <summary>
/// BlogListRules on their own: which order a filter asks for, the cursor written and read back, what comes after it,
/// and a month's range and label (ADR: Every LinkedIn post is also a blog post, the addendum on the read model).
/// </summary>
public sealed class BlogListRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 18, 11, 38, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("new", false, 'n')]
    [InlineData("new", true, 's')]
    [InlineData("read", true, 'r')]
    [InlineData("read", false, 'r')]
    [InlineData(null, false, 'n')]
    [InlineData("anything", true, 's')]
    public void Searching_ranks_by_score_unless_most_read_was_chosen(string? sort, bool searching, char order) =>
        Assert.Equal(order, BlogListRules.OrderOf(sort, searching));

    [Fact]
    public void A_cursor_reads_back_as_written_and_only_for_its_own_order()
    {
        var place = BlogListRules.Place('r', 12, At, 15);
        string written = BlogListRules.Write(place);
        Assert.Equal($"r.12.{At.UtcTicks}.15", written);
        Assert.Equal(place, BlogListRules.Read(written, 'r'));
        Assert.Null(BlogListRules.Read(written, 'n'));
        Assert.Equal(0, BlogListRules.Place('n', 12, At, 15).Rank);
        foreach (string? wrong in new[] { null, "", "r.12.15", "r.-1.5.5", "r.a.5.5", "rr.1.5.5", "r.1.5.5.5" })
        {
            Assert.Null(BlogListRules.Read(wrong, 'r'));
        }
    }

    [Fact]
    public void After_a_cursor_comes_a_lower_rank_then_an_older_post_then_a_lower_id()
    {
        var cursor = BlogListRules.Place('r', 5, At, 10);
        Assert.True(BlogListRules.After(BlogListRules.Place('r', 4, At.AddYears(1), 99), cursor));
        Assert.True(BlogListRules.After(BlogListRules.Place('r', 5, At.AddSeconds(-1), 99), cursor));
        Assert.True(BlogListRules.After(BlogListRules.Place('r', 5, At, 9), cursor));
        Assert.False(BlogListRules.After(BlogListRules.Place('r', 5, At, 10), cursor));
        Assert.False(BlogListRules.After(BlogListRules.Place('r', 5, At, 11), cursor));
        Assert.False(BlogListRules.After(BlogListRules.Place('r', 6, At.AddYears(-1), 1), cursor));
        var ordered = BlogListRules.Ordered([BlogListRules.Place('r', 1, At, 1), BlogListRules.Place('r', 2, At, 1), BlogListRules.Place('r', 2, At, 2), BlogListRules.Place('r', 2, At.AddDays(1), 0)]);
        Assert.Equal([(2, 0), (2, 2), (2, 1), (1, 1)], ordered.Select(p => (p.Rank, p.Id)));
    }

    [Fact]
    public void A_month_is_its_utc_calendar_month_with_a_label_and_anything_else_is_no_month()
    {
        Assert.Equal("2026-11", BlogListRules.MonthOf(At));
        Assert.Equal("2026-10", BlogListRules.MonthOf(new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.FromHours(-6)).AddHours(-5)));
        Assert.Equal("2026-11", BlogListRules.MonthOf(new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.FromHours(-6))));
        Assert.Equal((new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)), BlogListRules.MonthRange("2026-02"));
        Assert.Null(BlogListRules.MonthRange("2026-13"));
        Assert.Null(BlogListRules.MonthRange("november"));
        Assert.Null(BlogListRules.MonthRange(null));
        Assert.Equal("November 2026", BlogListRules.MonthLabel("2026-11"));
        Assert.Equal("January 2027", BlogListRules.MonthLabel("2027-01"));
        Assert.Equal("nonsense", BlogListRules.MonthLabel("nonsense"));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), BlogListRules.YearRange(2026)!.Value.To);
        Assert.Null(BlogListRules.YearRange(1999));
        Assert.Null(BlogListRules.YearRange(null));
    }
}
