using TheYard.Application.Features.Review;
using TheYard.Infrastructure.Cosmos.Features.Review;

namespace TheYard.Tests;

/// <summary>
/// The one rule the review route's reach is kept by (ADR: Ten minutes, seven stops, the addendum on the
/// reach rows): a higher stop replaces a lower one and never the reverse, the first time and the first
/// code stay, the last time moves, and stop 0 is the route opened.
/// </summary>
public sealed class ReviewFoldingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static ReviewReach Reach(string hash, int stop, string? code, int minutes, string day = "2026-11-04") =>
        new(hash, day, stop, code, Noon.AddMinutes(minutes));

    [Fact]
    public void A_lower_stop_never_lowers_a_row_and_a_higher_one_raises_it()
    {
        var kept = ReviewFolding.Row(Reach("a", 3, "zzrev1", 0));
        var lower = ReviewFolding.Merge(kept, ReviewFolding.Row(Reach("a", 1, null, 5)));
        Assert.Equal(3, lower.Stop);
        Assert.Equal(Noon.AddMinutes(5), lower.Last);
        var higher = ReviewFolding.Merge(lower, ReviewFolding.Row(Reach("a", 6, null, 9)));
        Assert.Equal(6, higher.Stop);
    }

    [Fact]
    public void The_first_time_and_the_first_code_stay_and_the_last_time_moves()
    {
        var folded = Assert.Single(ReviewFolding.Fold([
            Reach("a", 2, null, 4),
            Reach("a", 0, "zzrev1", 0),
            Reach("a", 5, "zzother", 11),
        ]));
        Assert.Equal(("a", 5, "zzrev1"), (folded.Hash, folded.Stop, folded.Code));
        Assert.Equal(Noon, folded.First);
        Assert.Equal(Noon.AddMinutes(11), folded.Last);
    }

    [Fact]
    public void A_batch_folds_to_one_row_a_visitor_a_day_and_drops_what_is_not_a_stop()
    {
        var rows = ReviewFolding.Fold([
            Reach("a", 0, null, 0),
            Reach("a", 1, null, 1),
            Reach("b", 0, null, 2),
            Reach("a", 2, null, 0, day: "2026-11-05"),
            Reach("c", 8, null, 3),
            Reach("c", -1, null, 3),
        ]);
        Assert.Equal(
            [("2026-11-04", "a", 1), ("2026-11-04", "b", 0), ("2026-11-05", "a", 2)],
            rows.Select(row => (row.Day, row.Hash, row.Stop)));
    }

    [Fact]
    public void The_actions_are_a_set_that_only_grows_and_a_row_kept_before_them_reads_as_none()
    {
        var folded = Assert.Single(ReviewFolding.Fold([
            Reach("a", 0, null, 0),
            Reach("a", 0, null, 1) with { Action = "proof" },
            Reach("a", 0, null, 2) with { Action = "book" },
            Reach("a", 0, null, 3) with { Action = "proof" },
        ]));
        Assert.Equal(["proof", "book"], folded.Actions!);

        // A row the store kept before actions were has none; folding a new one in adds, and nothing takes away.
        var old = new ReviewRow("a", "2026-11-04", 2, null, Noon, Noon);
        Assert.Null(old.Actions);
        var merged = ReviewFolding.Merge(old, ReviewFolding.Row(Reach("a", 0, null, 5) with { Action = "resume" }));
        Assert.Equal(["resume"], merged.Actions!);
        Assert.Equal(2, merged.Stop);
        var later = ReviewFolding.Merge(merged, ReviewFolding.Row(Reach("a", 1, null, 6)));
        Assert.Equal(["resume"], later.Actions!);
        Assert.Null(ReviewFolding.Merge(old, old).Actions);
    }

    [Fact]
    public void The_two_figures_read_a_stop_past_the_opening_or_an_action_and_a_resume_alone_is_not_the_running_system()
    {
        static ReviewRow Row(int stop, params string[]? actions) => new("a", "2026-11-04", stop, null, Noon, Noon, actions);

        Assert.False(ReviewActions.ReachedASecond(Row(0, null)));
        Assert.False(ReviewActions.ReachedASecond(Row(0)));
        Assert.True(ReviewActions.ReachedASecond(Row(0, "resume")));
        Assert.True(ReviewActions.ReachedASecond(Row(1)));
        Assert.False(ReviewActions.ReachedRunningOrBook(Row(0, "resume", "linkedin", "tour")));
        Assert.True(ReviewActions.ReachedRunningOrBook(Row(0, "book")));
        Assert.True(ReviewActions.ReachedRunningOrBook(Row(0, "slip")));
        Assert.True(ReviewActions.ReachedRunningOrBook(Row(3, null)));
        Assert.True(ReviewActions.ReachedRunningOrBook(Row(0, "gate-run")));
        Assert.False(ReviewActions.ReachedRunningOrBook(Row(0, "blog")));
        Assert.Equal(14, ReviewActions.Names.Count);
        Assert.Equal(["gate-run", "blog"], ReviewActions.Names.Skip(12));
        Assert.All(ReviewActions.RunningOrBook, name => Assert.True(ReviewActions.IsOne(name)));
        Assert.False(ReviewActions.IsOne("Proof"));
        Assert.False(ReviewActions.IsOne(null));
    }

    [Fact]
    public void The_document_keeps_the_actions_and_the_screen_and_one_written_before_them_reads_as_none()
    {
        // The mapping alone, both ways: no store is touched (RULE 79), the folding above is what a write runs.
        var row = new ReviewRow("a", "2026-11-04", 2, "zzrev1", Noon, Noon.AddMinutes(3), ["proof", "book"], "mirror");
        var document = CosmosReviewStore.DocumentFor(row);
        Assert.Equal(["proof", "book"], document.Actions!);
        Assert.Equal("mirror", document.Screen);
        var back = CosmosReviewStore.RowOf(document);
        Assert.Equal((row.Stop, row.Code, row.First, row.Last, row.Screen), (back.Stop, back.Code, back.First, back.Last, back.Screen));
        Assert.Equal(row.Actions!, back.Actions!);

        // A document kept before the second actions has neither field: no set, and the verdict's screen.
        var old = CosmosReviewStore.RowOf(CosmosReviewStore.DocumentFor(new ReviewRow("b", "2026-11-04", 0, null, Noon, Noon)));
        Assert.Null(old.Actions);
        Assert.Equal("verdict", ReviewActions.ScreenOf(old));
        Assert.False(ReviewActions.ReachedASecond(old));
    }

    [Fact]
    public void A_union_keeps_each_name_once_in_the_list_s_order_and_drops_a_name_no_page_reports()
    {
        Assert.Equal(["proof", "book", "slip"], ReviewActions.Union(["slip", "book"], ["proof", "slip"])!);
        Assert.Equal(["resume"], ReviewActions.Union(["resume", "elsewhere"], null)!);
        Assert.Equal([], ReviewActions.Union([], null)!);
        Assert.Null(ReviewActions.Union(null, null));
    }

    [Theory]
    [InlineData("verdict", "verdict")]
    [InlineData("mirror", "mirror")]
    [InlineData("Mirror", null)]
    [InlineData("ai", null)]
    [InlineData(null, null)]
    public void A_screen_is_one_the_route_draws_or_none(string? reported, string? kept)
    {
        Assert.Equal(kept, ReviewActions.ScreenOrNull(reported));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, false)]
    public void A_stop_is_opened_or_one_of_the_seven(int stop, bool isOne)
    {
        Assert.Equal(isOne, ReviewFolding.IsStop(stop));
    }
}
