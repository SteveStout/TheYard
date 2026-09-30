using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TestProject.Library;

namespace TestProject.Tests;

/// <summary>
/// Checks the project's documents and how the app serves them. The decision records in docs
/// (files named ADR-NNN-*.md) must be numbered from 1 with no gap and share one layout. The
/// document list at /api/docs must offer every record and guide, and each must load as markdown.
/// Every code block that pulls in live source must find its file and region, every GitHub link
/// must point at a file that exists with the same letter case, every listed document must be
/// published with the app, and /api/version must report the newest changelog line. The documents
/// describe the code, so these checks fail the build when the two drift apart.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
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
                // Compare each path segment with exact case. Windows finds a file whatever the
                // case, but GitHub, which serves these links, does not, so a wrong-case link would
                // pass a plain File.Exists check on Windows and still be broken online.
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
        // The deployed app reads documents from its published output, not the source folder.
        // A document the project file does not copy on publish passes every other test here,
        // because they read the source folder, yet is missing on the live site. So this reads the
        // Content items in TestProject.csproj and checks that each listed document matches one of
        // their Include patterns. The patterns use backslashes, so the paths are converted first.
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
