using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Application.Features.Admin.Foreman;

namespace TheYard.Tests;

/// <summary>
/// The Foreman at rung 0 (ADR: The Foreman, a place to start): one page in five sections, in order, every reading
/// with its source and every place to start a record, a file and a test that exist; computed from a recorded night
/// to the same page every time; and served on two routes, the page and its markdown.
/// </summary>
public sealed class ForemanTests
{
    private static readonly long Now = new DateTimeOffset(2026, 10, 25, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>A recorded night: the share amber, the campaign unread, everything else fine, one failure in the hour.</summary>
    private static ForemanPage Night(long startedMinutesAgo, bool insights = true) => ForemanPages.Compose(
        [
            new ForemanCard("sqldb", "Azure SQL Database", "fine", "0 %", null),
            new ForemanCard("share", "The job-hunt share", "amber", "19,032", null),
            new ForemanCard("campaign", "The email campaign", "unread", null, "the campaign database did not answer"),
        ],
        insights
            ? new ForemanFailures(true, null, [new ForemanFailure("app-theyard-ss", "System.TimeoutException", "GET /api/vehicles", 2, Now - 600_000)])
            : new ForemanFailures(false, "Application Insights did not answer", []),
        new ForemanBuild("1.0.4.82", Now - (startedMinutesAgo * 60_000), Now));

    [Fact]
    public void A_page_has_the_five_sections_in_order_and_each_reading_names_its_source()
    {
        string page = Night(20).Markdown;
        string[] headings = [.. page.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal)).Select(line => line[3..])];
        Assert.Equal(["What happened", "The readings", "The likely cause", "Where to start", "What it could not read"], headings);
        Assert.StartsWith("# The Foreman, 2026-10-25", page, StringComparison.Ordinal);
        Assert.Contains("| The job-hunt share | amber | 19,032 |  | /api/admin/cloud/share?window=24h |", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Azure SQL Database", page, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)0x2014).ToString(), page, StringComparison.Ordinal);
    }

    [Fact]
    public void Rung_0_names_no_cause_says_what_is_nearest_and_what_it_ruled_out_and_what_it_could_not_read()
    {
        var early = Night(20);
        Assert.StartsWith("In the 24 hours to Sun 25 Oct 13:00 Central, 0 of 3 Cloud infrastructure cards read red, 1 amber and 1 unread; Application Insights kept 2 failures in 7 days, 2 in the last hour", early.What, StringComparison.Ordinal);
        Assert.StartsWith("No cause with confidence. Nearest: this process started 20 minutes ago", early.Cause, StringComparison.Ordinal);
        Assert.Contains("the newest failure, System.TimeoutException in GET /api/vehicles on app-theyard-ss, was at Sun 25 Oct 12:50 Central", early.Cause, StringComparison.Ordinal);
        Assert.Equal(["The email campaign: the campaign database did not answer"], early.CouldNotRead);

        var late = Night(300, insights: false);
        Assert.Contains("Ruled out: a start in the last hour (this process started Sun 25 Oct 08:00)", late.Cause, StringComparison.Ordinal);
        Assert.Contains("Application Insights: Application Insights did not answer", late.CouldNotRead);
    }

    [Fact]
    public void Where_to_start_names_a_record_a_file_and_a_test_for_every_card_and_each_exists()
    {
        var page = Night(20);
        Assert.Equal(["The job-hunt share", "The email campaign"], page.Start.Select(row => row.Card));
        string root = Repo.Root();
        var titles = Directory.EnumerateFiles(Path.Combine(root, "docs", "decisions"), "ADR-*.md")
            .ToDictionary(path => Path.GetFileName(path)[..7], path => File.ReadLines(path).First()["# ADR: ".Length..], StringComparer.Ordinal);
        foreach (var (key, (record, file, test)) in ForemanPages.Starts)
        {
            string number = record[..7];
            Assert.True(titles.TryGetValue(number, out string? title), $"{key} names {number}, which is not a record");
            Assert.Equal(title, record[9..]);
            Assert.True(File.Exists(Path.Combine(root, file)), $"{key} names {file}, which is not there");
            Assert.True(File.Exists(Path.Combine(root, test)), $"{key} names {test}, which is not there");
        }
    }

    [Fact]
    public async Task The_handler_reads_the_cards_the_failures_and_the_build_through_its_one_port()
    {
        var page = await new ReadForemanHandler(new Fake()).HandleAsync(new ReadForemanQuery(), CancellationToken.None);
        Assert.Equal("1.0.4.82", page.Version);
        Assert.Equal("Nothing moved: every card reads fine.", page.Cause);
        Assert.Equal("Nowhere: nothing moved.", page.Markdown.Split("## Where to start\n\n")[1].Split('\n')[0]);
    }

    private sealed class Fake : IForemanReadings
    {
        public Task<IReadOnlyList<ForemanCard>> CardsAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<ForemanCard>>([new ForemanCard("sqldb", "Azure SQL Database", "fine", "0 %", null)]);

        public Task<ForemanFailures> FailuresAsync(CancellationToken cancellation) => Task.FromResult(new ForemanFailures(true, null, []));

        public ForemanBuild Build() => new("1.0.4.82", Now - 7_200_000, Now);
    }
}

/// <summary>The Foreman's two routes, the page and its markdown, served by the host as a visitor asks them.</summary>
public sealed class ForemanRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/admin/foreman", 200)]
    [Covers("GET", "/api/admin/foreman.md", 200)]
    public async Task The_page_and_its_markdown_are_served_and_off_Azure_say_what_they_could_not_read()
    {
        using var client = factory.CreateClient();
        var page = await client.GetAsync("/api/admin/foreman");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var body = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Contains("Application Insights", string.Join(" ", body.RootElement.GetProperty("could_not_read").EnumerateArray().Select(line => line.GetString())), StringComparison.Ordinal);

        var markdown = await client.GetAsync("/api/admin/foreman.md");
        Assert.Equal(HttpStatusCode.OK, markdown.StatusCode);
        Assert.Equal("text/markdown", markdown.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("# The Foreman, ", await markdown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
