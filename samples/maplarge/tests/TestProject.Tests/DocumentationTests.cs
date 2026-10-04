using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TestProject.Documentation;

namespace TestProject.Tests;

/// <summary>
/// Checks the project's documents and how the app serves them. The decision records in docs
/// (files named ADR-NNN-*.md) must be numbered from 1 with no gap and share one layout. The
/// document list at /api/docs must offer every record and guide, and each must load as markdown.
/// Every code block that pulls in live source must find its file and region, every GitHub link
/// must point at a file that exists with the same letter case, and /api/version must report the
/// newest changelog line. The documents describe the code, so these checks fail the build when
/// the two drift apart. What a publish carries is checked in PublishListTests.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed partial class DocumentationTests : IDisposable
{
    /// <summary>The app booted in memory, so a test requests documents the way the Docs tab does.</summary>
    private readonly WebApplicationFactory<Program> _factory = new();
    /// <summary>The docs folder of this build.</summary>
    private readonly string _docs = Path.Combine(ProjectFolder.Root(), "docs");

    /// <summary>Shuts the in-memory app down after each test.</summary>
    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Every decision record file, in number order.</summary>
    private static List<string> Records() =>
        Directory.EnumerateFiles(Path.Combine(ProjectFolder.Root(), "docs"), "ADR-*.md").OrderBy(DocumentationCatalog.RecordNumber).ToList();

    [Fact]
    public void Record_numbers_run_from_one_with_no_gap()
    {
        List<int> numbers = Records().Select(DocumentationCatalog.RecordNumber).ToList();
        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
    }

    [Fact]
    public void Every_record_has_a_title_a_status_line_and_ends_with_its_files()
    {
        var wrong = new List<string>();
        foreach (string file in Records())
        {
            string[] lines = File.ReadAllLines(file);
            string name = Path.GetFileName(file);
            if (!lines[0].StartsWith("# ADR: ", StringComparison.Ordinal))
            {
                wrong.Add($"{name} does not open with '# ADR: '");
            }
            if (!lines.Any(line => line.StartsWith("Status: accepted, 2026-", StringComparison.Ordinal)))
            {
                wrong.Add($"{name} has no 'Status: accepted, <date>' line");
            }
            int files = Array.FindIndex(lines, line => line == "## Files");
            if (files < 0 || lines.Skip(files + 1).Any(line => line.StartsWith("## ", StringComparison.Ordinal)))
            {
                wrong.Add($"{name} does not end with a '## Files' section");
            }
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_record_says_where_it_sits_just_above_its_files()
    {
        // Which ring the decision touches, which SOLID idea it follows, what it cost and what
        // would change it, in one short paragraph a reader meets before the list of files.
        var wrong = new List<string>();
        foreach (string file in Records())
        {
            string[] lines = File.ReadAllLines(file);
            string name = Path.GetFileName(file);
            int sits = Array.FindIndex(lines, line => line == "## Where it sits");
            int files = Array.FindIndex(lines, line => line == "## Files");
            bool once = lines.Count(line => line == "## Where it sits") == 1;
            bool justAbove = sits >= 0 && files > sits && !lines.Skip(sits + 1).Take(files - sits - 1).Any(line => line.StartsWith("## ", StringComparison.Ordinal));
            bool said = sits >= 0 && string.Join(" ", lines.Skip(sits + 1).Take(Math.Max(0, files - sits - 1))).Trim().Length >= 40;
            if (!once || !justAbove || !said)
            {
                wrong.Add($"{name} needs one '## Where it sits' paragraph directly above '## Files'");
            }
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public async Task The_catalogue_serves_every_record_and_every_guide()
    {
        using HttpClient client = _factory.CreateClient();
        JsonElement catalogue = JsonDocument.Parse(await client.GetStringAsync("/api/docs")).RootElement;
        List<string> slugs = catalogue.EnumerateArray().Select(entry => entry.GetProperty("slug").GetString()!).ToList();
        foreach (string file in Records())
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), slugs);
        }
        Assert.Contains("start-here", slugs);
        Assert.Contains("readme", slugs);
        Assert.Contains("style", slugs);
        Assert.Contains("built-with-ai", slugs);
        Assert.Contains("changelog", slugs);
        foreach (string slug in slugs)
        {
            HttpResponseMessage response = await client.GetAsync($"/api/docs/{slug}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        }
        HttpResponseMessage missing = await client.GetAsync("/api/docs/not-a-document");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Every_live_fence_resolves_to_code_from_this_build()
    {
        using HttpClient client = _factory.CreateClient();
        var unresolved = new List<string>();
        foreach (string file in Directory.EnumerateFiles(_docs, "*.md").Append(Path.Combine(ProjectFolder.Root(), "README.md")))
        {
            string markdown = File.ReadAllText(file);
            if (!LiveSamples.Fences(markdown).Any())
            {
                continue;
            }
            string slug = Path.GetFileName(file) == "README.md" ? "readme" : Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            string served = await client.GetStringAsync($"/api/docs/{slug}");
            foreach (Match note in SampleUnavailable().Matches(served))
            {
                unresolved.Add($"{Path.GetFileName(file)}: {note.Value}");
            }
        }
        Assert.Empty(unresolved);
    }

    [Fact]
    public void Every_repository_link_in_a_document_points_at_a_file_that_exists()
    {
        var broken = new List<string>();
        foreach (string file in Directory.EnumerateFiles(_docs, "*.md").Append(Path.Combine(ProjectFolder.Root(), "README.md")))
        {
            foreach (Match link in RepoLink().Matches(File.ReadAllText(file)))
            {
                // Compare each path segment with exact case. Windows finds a file whatever the
                // case, but GitHub, which serves these links, does not, so a wrong-case link would
                // pass a plain File.Exists check on Windows and still be broken online.
                if (!ExistsExact(ProjectFolder.Root(), link.Groups["path"].Value))
                {
                    broken.Add($"{Path.GetFileName(file)} -> {link.Groups["path"].Value}");
                }
            }
        }
        Assert.Empty(broken);
    }

    [Fact]
    public async Task The_version_is_the_changelog_top_line()
    {
        using HttpClient client = _factory.CreateClient();
        JsonElement version = JsonDocument.Parse(await client.GetStringAsync("/api/version")).RootElement;
        string expected = VersionReader.VersionFrom(Path.Combine(_docs, "CHANGELOG.md"));
        Assert.NotEqual(VersionReader.Unknown, expected);
        Assert.Equal(expected, version.GetProperty("version").GetString());
        Assert.False(string.IsNullOrEmpty(version.GetProperty("commit").GetString()));
    }

    [Fact]
    public void The_changelog_is_newest_first_and_one_line_per_version()
    {
        List<Version> versions = File.ReadLines(Path.Combine(_docs, "CHANGELOG.md"))
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => Version.Parse(FourNumbers().Match(line).Value))
            .ToList();
        Assert.NotEmpty(versions);
        Assert.Equal(versions.OrderByDescending(v => v), versions);
        Assert.Equal(versions.Distinct().Count(), versions.Count);
    }

    /// <summary>Whether a relative path exists with exactly this letter case, one segment at a time.</summary>
    private static bool ExistsExact(string root, string relative)
    {
        string current = root;
        foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string? match = Directory.EnumerateFileSystemEntries(current)
                .Select(entry => Path.GetFileName(entry))
                .FirstOrDefault(name => string.Equals(name, segment, StringComparison.Ordinal));
            if (match is null)
            {
                return false;
            }
            current = Path.Combine(current, match);
        }
        return true;
    }

    /// <summary>The note a live block becomes when its file or region cannot be found.</summary>
    [GeneratedRegex(@"> Sample unavailable: [^\n]*")]
    private static partial Regex SampleUnavailable();

    /// <summary>A link to a file of this sample on GitHub, with the path inside the sample captured.</summary>
    [GeneratedRegex(@"https://github\.com/SteveStout/TheYard/(?:blob|tree)/main/samples/maplarge/(?<path>[^)\s#]+)")]
    private static partial Regex RepoLink();

    /// <summary>A four-number version such as 1.0.0.28.</summary>
    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex FourNumbers();
}
