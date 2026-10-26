using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The first card under 05 What broke? (ADR: A red that lasts an hour gets a postmortem): every incident the docs
/// keep, read from the index itself, and the failures Application Insights kept, read from the workspace; nothing
/// typed by hand, and no failure's message, which can carry what a visitor sent.
/// </summary>
public sealed class IncidentsCardTests
{
    private static readonly long Now = new DateTimeOffset(2026, 10, 25, 17, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private sealed class Reader(string index, FailuresRead failures) : IIncidentsReader
    {
        public string IncidentsIndex() => index;

        public Task<FailuresRead> FailuresAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(failures);
    }

    private static FailureRow Failure(long agoMinutes, long count = 1) =>
        new("app-theyard-ss", "exception", "System.TimeoutException", "GET /api/vehicles", count, Now - (agoMinutes * 60_000));

    [Fact]
    public void The_card_lists_every_incident_the_index_lists_with_its_document()
    {
        string index = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "incidents", "INCIDENTS.md"));
        var incidents = ReadIncidentsHandler.Parse(index);
        int files = Directory.EnumerateFiles(Path.Combine(Repo.Root(), "docs", "incidents"), "INC-*.md").Count();
        Assert.Equal(files, incidents.Count);
        var first = Assert.Single(incidents, incident => incident.Id == "INC-001");
        Assert.Equal("inc-001-the-share-that-billed-for-looking", first.Slug);
        Assert.Equal("The share that billed for looking", first.Title);
        Assert.Contains("1.0.4.49", first.FixedIn, StringComparison.Ordinal);
        Assert.True(DocumentationCatalogHas(first.Slug));
    }

    [Theory]
    [InlineData(10, "red")]
    [InlineData(600, "amber")]
    public async Task A_failure_in_the_last_hour_is_red_and_one_in_the_week_is_amber(long agoMinutes, string tone)
    {
        var report = await new ReadIncidentsHandler(new Reader("", new FailuresRead(true, null, Now, [Failure(agoMinutes, 3)])))
            .HandleAsync(new ReadIncidentsQuery(), CancellationToken.None);
        Assert.Equal(tone, report.Tone);
        Assert.Equal("3 failures in 7 days", report.Headline);
        Assert.Equal(Now, report.AsOfMs);
    }

    [Fact]
    public async Task No_failure_is_fine_and_an_unread_workspace_says_why_and_still_lists_the_incidents()
    {
        string index = "| [INC-001](https://theyard.stevenstout.biz/?doc=inc-001-x) | X | 1 Oct | none | none | 1.0.4.49 |\n";
        var fine = await new ReadIncidentsHandler(new Reader(index, new FailuresRead(true, null, Now, [])))
            .HandleAsync(new ReadIncidentsQuery(), CancellationToken.None);
        Assert.Equal("fine", fine.Tone);
        Assert.Equal("0 failures in 7 days", fine.Headline);

        var unread = await new ReadIncidentsHandler(new Reader(index, new FailuresRead(false, "Application Insights did not answer", Now, [])))
            .HandleAsync(new ReadIncidentsQuery(), CancellationToken.None);
        Assert.Equal("unread", unread.Tone);
        Assert.Equal("Application Insights did not answer", unread.FailuresNote);
        Assert.Equal("1 incident", unread.Headline);
        Assert.Single(unread.Incidents);
    }

    [Fact]
    public async Task The_card_lists_at_most_thirty_failures()
    {
        var rows = Enumerable.Range(0, 40).Select(i => Failure(i)).ToList();
        var report = await new ReadIncidentsHandler(new Reader("", new FailuresRead(true, null, Now, rows)))
            .HandleAsync(new ReadIncidentsQuery(), CancellationToken.None);
        Assert.Equal(ReadIncidentsHandler.MaxFailures, report.Failures.Count);
    }

    [Fact]
    public void The_query_reads_exceptions_and_answers_of_500_or_more_and_never_a_message()
    {
        string kusto = InsightsIncidents.Kusto(ReadIncidentsHandler.Window);
        Assert.Contains("ago(168h)", kusto, StringComparison.Ordinal);
        Assert.Contains("AppExceptions", kusto, StringComparison.Ordinal);
        Assert.Contains("toint(ResultCode) >= 500", kusto, StringComparison.Ordinal);
        Assert.Contains("top 30 by Last desc", kusto, StringComparison.Ordinal);
        Assert.DoesNotContain("Message", kusto, StringComparison.Ordinal);
        Assert.DoesNotContain("Url", kusto, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_of_the_answer_is_one_failure()
    {
        var row = InsightsIncidents.Row(new Dictionary<string, string?>
        {
            ["Role"] = "func-jobhunt-ss",
            ["Kind"] = "answer",
            ["What"] = "500",
            ["Where"] = "Merger",
            ["Count"] = "2",
            ["Last"] = "2026-10-25T17:20:00Z",
        });
        Assert.Equal(new FailureRow("func-jobhunt-ss", "answer", "500", "Merger", 2, Now - 600_000), row);
    }

    private static bool DocumentationCatalogHas(string slug) =>
        TheYard.Api.Shared.Documentation.DocumentationCatalog.Files.ContainsKey(slug);
}
