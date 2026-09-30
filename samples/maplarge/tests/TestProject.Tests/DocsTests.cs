using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TestProject.Library;

namespace TestProject.Tests;

/// <summary>
/// The records are served, whole, numbered and current (ADR-012, ADR-009): the
/// catalogue offers every ADR file and nothing unnumbered, the numbers run from
/// one with no gap, every record keeps its shape, every live fence resolves,
/// every repository link lands on a file, and the version the API reports is
/// the changelog's newest line.
/// </summary>
public sealed partial class DocsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly string _docs = Path.Combine(Repo.Root(), "docs");

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static List<string> Records() =>
        Directory.EnumerateFiles(Path.Combine(Repo.Root(), "docs"), "ADR-*.md").OrderBy(DocsCatalog.RecordNumber).ToList();

    [Fact]
    public void Record_numbers_run_from_one_with_no_gap()
    {
        List<int> numbers = Records().Select(DocsCatalog.RecordNumber).ToList();
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
        foreach (string file in Directory.EnumerateFiles(_docs, "*.md").Append(Path.Combine(Repo.Root(), "README.md")))
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
        foreach (string file in Directory.EnumerateFiles(_docs, "*.md").Append(Path.Combine(Repo.Root(), "README.md")))
        {
            foreach (Match link in RepoLink().Matches(File.ReadAllText(file)))
            {
                // Exact case, segment by segment: Windows would say a link with the wrong case exists,
                // and GitHub, which serves the link, would not (1.0.0.2 learned this the hard way).
                if (!ExistsExact(Repo.Root(), link.Groups["path"].Value))
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

    [Fact]
    public void Every_document_the_catalogue_serves_is_published_with_the_app()
    {
        // The container is the content root at runtime, so a document the project file does not
        // publish is missing there and nowhere else: README.md was, and the live site answered
        // its slug with a 409 while every test here, reading the source folder, passed (1.0.0.10).
        string root = Repo.Root();
        List<Regex> published = System.Xml.Linq.XDocument.Load(Path.Combine(root, "TestProject.csproj"))
            .Descendants("Content")
            .Where(item => item.Attribute("CopyToPublishDirectory") is not null && item.Attribute("Include") is not null)
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';'))
            .Select(GlobToRegex)
            .ToList();
        var catalogue = new DocsCatalog(root);
        List<string> unpublished = catalogue.List()
            .Select(entry => Path.GetRelativePath(root, catalogue.FileFor(entry.Slug)!).Replace('/', '\\'))
            .Where(path => !published.Any(glob => glob.IsMatch(path)))
            .ToList();
        Assert.Empty(unpublished);
    }

    private static Regex GlobToRegex(string glob) =>
        new("^" + Regex.Escape(glob.Trim()).Replace(@"\*\*\\", @"(.*\\)?", StringComparison.Ordinal).Replace(@"\*", @"[^\\]*", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"> Sample unavailable: [^\n]*")]
    private static partial Regex SampleUnavailable();

    [GeneratedRegex(@"https://github\.com/SteveStout/TheYard/(?:blob|tree)/main/samples/maplarge/(?<path>[^)\s#]+)")]
    private static partial Regex RepoLink();

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex FourNumbers();
}
