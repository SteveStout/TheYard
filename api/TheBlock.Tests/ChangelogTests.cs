using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

/// <summary>
/// The changelog (ADR-012): one file, one line per shipped version, newest
/// first. The endpoint serves it and the file keeps its shape.
/// </summary>
public class ChangelogTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>- **1.0.0.N** (YYYY-MM-DD): one sentence.</summary>
    private static readonly Regex EntryLine =
        new(@"^- \*\*1\.0\.0\.(\d+)\*\* \(\d{4}-\d{2}-\d{2}\): (.+)$", RegexOptions.Compiled);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Changelog_and_its_decision_record_are_served_as_markdown()
    {
        var changelog = await _client.GetAsync("/api/docs/changelog");
        Assert.Equal(HttpStatusCode.OK, changelog.StatusCode);
        Assert.Equal("text/markdown", changelog.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("# Changelog", await changelog.Content.ReadAsStringAsync());

        var adr = await _client.GetAsync("/api/docs/adr-changelog");
        Assert.Equal(HttpStatusCode.OK, adr.StatusCode);
        Assert.Contains("# ADR: The changelog", await adr.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_version_has_one_line_newest_first_with_no_repeats()
    {
        string markdown = await _client.GetStringAsync("/api/docs/changelog");
        Assert.DoesNotContain("\u2014", markdown); // the house rule: no em dash in anything served

        var versions = new List<int>();
        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;

            var match = EntryLine.Match(line);
            Assert.True(match.Success, $"changelog line does not fit the one-line shape: {line}");
            versions.Add(int.Parse(match.Groups[1].Value));
            Assert.EndsWith(".", match.Groups[2].Value);
        }

        Assert.True(versions.Count >= 14, "1.0.0.1 through 1.0.0.14 are the floor");
        Assert.Equal(versions.OrderByDescending(v => v).ToList(), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(1, versions[^1]);
    }
}
