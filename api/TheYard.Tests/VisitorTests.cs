using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Application.Features.Visitors;

namespace TheYard.Tests;

// #region visitor-tests
/// <summary>
/// People who have visited, counted once (ADR-116): the kind rule the Activity card and the count share, the
/// hash that does not rotate, the collector's one sighting a visitor a day, and the count's query over a
/// store in memory with the day boundaries in UTC, and the visits by people the kept history holds from before the
/// count began (the addendum of 1.0.4.48).
/// </summary>
public sealed class VisitorTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 23, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_visitor_whose_every_request_looked_like_a_bot_is_a_scanner_and_the_site_itself_is_never_a_person()
    {
        Assert.Equal(VisitorKind.People, VisitorKinds.Of(false, 3, 2));
        Assert.Equal(VisitorKind.Scanners, VisitorKinds.Of(false, 3, 3));
        Assert.Equal(VisitorKind.Self, VisitorKinds.Of(true, 3, 0));
        Assert.Equal(VisitorKind.People, VisitorKinds.Stronger(VisitorKind.Scanners, VisitorKind.People));
        Assert.Equal(VisitorKind.Self, VisitorKinds.Stronger(VisitorKind.People, VisitorKind.Self));
        Assert.Equal(VisitorKind.Scanners, VisitorKinds.FromWord("anything else"));
    }

    [Fact]
    public void The_hash_is_the_same_every_day_and_is_neither_the_address_nor_the_daily_token()
    {
        var tokens = new VisitorTokens("test-key");
        string person = tokens.PersonFor("203.0.113.7", "Mozilla/5.0");

        Assert.Equal(person, tokens.PersonFor("203.0.113.7", "Mozilla/5.0"));
        Assert.NotEqual(person, tokens.PersonFor("203.0.113.7", "curl/8.0"));
        Assert.NotEqual(person, tokens.TokenFor("203.0.113.7", Today));
        Assert.DoesNotContain("203.0.113", person, StringComparison.Ordinal);
        Assert.Equal(32, person.Length);
    }

    [Fact]
    public async Task The_collector_hands_on_one_sighting_a_visitor_a_day_and_none_for_the_site_itself()
    {
        var visitors = new MemoryVisitorStore();
        var collector = new ActivityCollector(new Dictionary<string, IActivityStore>(StringComparer.Ordinal) { ["sql"] = NullActivityStore.Instance }, "sql", NullLogger<ActivityCollector>.Instance, visitors);

        collector.Offer(new ActivityHit(Today, "t1", "203.0.113.x", "/", "sql", false) { Person = "a" });
        collector.Offer(new ActivityHit(Today, "t1", "203.0.113.x", "/about", "sql", false) { Person = "a" });
        collector.Offer(new ActivityHit(Today, "t2", "198.51.100.x", "/wp-login.php", "sql", true) { Person = "b" });
        collector.Offer(new ActivityHit(Today, "t3", "self", "/api/version", "sql", true));
        await collector.DrainAsync(CancellationToken.None);
        collector.Offer(new ActivityHit(Today.AddMinutes(5), "t1", "203.0.113.x", "/", "sql", false) { Person = "a" });
        await collector.DrainAsync(CancellationToken.None);

        var tally = await visitors.TallyAsync("2026-10-01", CancellationToken.None);
        Assert.Equal(1, tally.People);
        Assert.Equal(1, tally.Scanners);
        Assert.Equal(1, tally.FirstSeenByDay["2026-10-23"]);
    }

    [Fact]
    public async Task The_count_is_all_time_people_once_each_with_today_this_month_and_thirty_UTC_days()
    {
        var visitors = new MemoryVisitorStore();
        await visitors.KeepAsync(
            [
                new("a", "2026-10-20", VisitorKind.People),
                new("a", "2026-10-23", VisitorKind.People),
                new("b", "2026-10-23", VisitorKind.Scanners),
                new("c", "2026-10-01", VisitorKind.People),
                new("d", "2026-09-30", VisitorKind.People),
                new("e", "2026-10-23", VisitorKind.People),
            ],
            CancellationToken.None);

        var count = await new ReadPeopleCountHandler(visitors, new Fixed(Today)).HandleAsync(new ReadPeopleCountQuery(), CancellationToken.None);

        Assert.Equal(4, count.PeopleAllTime);
        Assert.Equal(1, count.PeopleNewToday);
        Assert.Equal(3, count.PeopleNewMonth);
        Assert.Equal(1, count.ScannersLeftOut);
        Assert.Equal("2026-09-30", count.CountedFrom);
        Assert.Equal(ReadPeopleCountHandler.Days, count.PeopleNewByDay.Count);
        Assert.Equal(("2026-10-23", 1), (count.PeopleNewByDay[^1].Day, count.PeopleNewByDay[^1].People));
        Assert.Equal("2026-09-24", count.PeopleNewByDay[0].Day);
        Assert.Equal(1, count.PeopleNewByDay.Single(day => day.Day == "2026-10-20").People);
        Assert.Equal(visitors.Source, count.Source);
    }

    [Fact]
    public async Task A_store_that_cannot_keep_visitors_is_unread_and_says_why()
    {
        var count = await new ReadPeopleCountHandler(new NoVisitorStore("no document store"), new Fixed(Today)).HandleAsync(new ReadPeopleCountQuery(), CancellationToken.None);
        Assert.Null(count.PeopleAllTime);
        Assert.Empty(count.PeopleNewByDay);
        Assert.Equal("no document store", count.Source);
    }

    [Fact]
    public async Task The_kept_history_counts_one_visit_a_person_a_day_and_leaves_out_scanners_and_the_site_itself()
    {
        static ActivityVisitor Row(string day, string token, string network, int requests, int bots) =>
            new("sql", day, token, network, Today, Today, requests, bots, new Dictionary<string, int>());
        ActivityVisitor[] rows =
        [
            // The same person on two days is two visits: the token rotates daily, so nothing kept can join them.
            Row("2026-09-24", "a1", "203.0.113", 4, 0),
            Row("2026-09-25", "a2", "203.0.113", 2, 1),
            // One person read through both stores the same day is one visit.
            Row("2026-09-25", "b1", "198.51.100", 1, 0),
            new("cosmos", "2026-09-25", "b1", "198.51.100", Today, Today, 3, 0, new Dictionary<string, int>()),
            // A scanner, the site's own tools, and two loopback rows that are one machine: none of them people.
            Row("2026-09-25", "s1", "192.0.2", 5, 5),
            Row("2026-09-25", "t1", Hits.SelfNetwork, 9, 0),
            Row("2026-09-26", "l1", "127.0.0", 1, 0),
            Row("2026-09-26", "l2", "127.0.0", 1, 0),
        ];
        var kept = VisitHistory.Count(rows);
        Assert.NotNull(kept);
        Assert.Equal((3, "2026-09-24"), (kept.Visits, kept.From));
        Assert.Equal(new Dictionary<string, int> { ["2026-09-24"] = 1, ["2026-09-25"] = 2 }, kept.ByDay);
        Assert.Null(VisitHistory.Count([]));

        var history = new VisitHistory(new RowsStore(rows), new Fixed(Today));
        Assert.Null(await history.ReadAsync(CancellationToken.None));
        await history.Taking;
        Assert.Equal(3, (await history.ReadAsync(CancellationToken.None))?.Visits);

        // A later count reads from the day before the newest day held and replaces those days: a day that grew is
        // counted again, a day before the read is kept as it was, and a window with no person in it counts as none.
        var held = new KeptVisits(3, "2026-09-24", new Dictionary<string, int> { ["2026-09-24"] = 1, ["2026-09-25"] = 2 });
        var later = new KeptVisits(5, "2026-09-25", new Dictionary<string, int> { ["2026-09-25"] = 3, ["2026-09-26"] = 2 });
        var merged = VisitHistory.Merge(held, later, new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal((6, "2026-09-24"), (merged.Visits, merged.From));
        Assert.Equal(new Dictionary<string, int> { ["2026-09-24"] = 1, ["2026-09-25"] = 3, ["2026-09-26"] = 2 }, merged.ByDay);
        var quiet = VisitHistory.Merge(held, null, new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal((1, "2026-09-24"), (quiet.Visits, quiet.From));
        Assert.Equal(new Dictionary<string, int> { ["2026-09-24"] = 1 }, quiet.ByDay);

        // The count carries them: the total, the first day, and the last thirty days with the kept days filled in.
        var count = await new ReadPeopleCountHandler(new MemoryVisitorStore(), new Fixed(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)), history).HandleAsync(new ReadPeopleCountQuery(), CancellationToken.None);
        Assert.NotNull(count.Visits);
        Assert.Equal((3, "2026-09-24"), (count.Visits.AllTime, count.Visits.From));
        Assert.Equal(30, count.Visits.ByDay.Count);
        Assert.Equal([1, 2, 0], count.Visits.ByDay.TakeLast(3).Select(day => day.Visits));
        Assert.Equal("2026-09-26", count.Visits.ByDay[^1].Day);
        // Without the history, the count says nothing about visits rather than zero.
        Assert.Null((await new ReadPeopleCountHandler(new MemoryVisitorStore(), new Fixed(Today)).HandleAsync(new ReadPeopleCountQuery(), CancellationToken.None)).Visits);
    }

    private sealed class RowsStore(IReadOnlyList<ActivityVisitor> rows) : IActivityStore
    {
        public Task<ActivityAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new ActivityAvailability(true, "kept"));

        public Task RecordAsync(IReadOnlyList<ActivityHit> hits, CancellationToken cancellation) => Task.CompletedTask;

        public Task<IReadOnlyList<ActivityHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<ActivityHour>>([]);

        public Task<IReadOnlyList<ActivityVisitor>> VisitorsAsync(DateTimeOffset since, CancellationToken cancellation) => Task.FromResult(rows);
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>The count's route through the real host (ADR-116), apart from the tests above so they build the handler with no host.</summary>
public sealed class VisitorRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/visitors/people", 200)]
    public async Task The_count_answers_200_with_every_figure_and_its_source()
    {
        var response = await factory.CreateClient().GetAsync("/api/admin/visitors/people");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (string field in new[] { "people_all_time", "people_new_today", "people_new_month", "scanners_left_out", "counted_from", "people_new_by_day", "source", "visits" })
        {
            Assert.True(body.RootElement.TryGetProperty(field, out _), $"the count has no {field}");
        }
    }
}
// #endregion visitor-tests
