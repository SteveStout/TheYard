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
/// must point at a file that exists with the same letter case, every listed document must be
/// published with the app, and /api/version must report the newest changelog line. The documents
/// describe the code, so these checks fail the build when the two drift apart.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed partial class DocumentationTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly string _docs = Path.Combine(ProjectFolder.Root(), "docs");

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

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
        string root = ProjectFolder.Root();
        List<Regex> published = System.Xml.Linq.XDocument.Load(Path.Combine(root, "TestProject.csproj"))
            .Descendants("Content")
            .Where(item => item.Attribute("CopyToPublishDirectory") is not null && item.Attribute("Include") is not null)
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';'))
            .Select(GlobToRegex)
            .ToList();
        var catalogue = new DocumentationCatalog(root);
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

/// <summary>
/// Checks LiveSamples, which pastes real source code into the served documents. A document can
/// hold an empty code block that names a file and a region, and the app fills it with the lines
/// between that region's start and end markers. The tests cover which paths may be read, how a
/// region is cut out, and what a bad block becomes. The path check uses the same rules as the file
/// browser's home guard on a shorter list of folders, because the document picks the path and must
/// never reach a file such as the development settings.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed class LiveSamplesTests
{
    [Theory]
    [InlineData("Domain/HomePath.cs", true)]
    [InlineData("Program.cs", true)]
    [InlineData("src/lib/urlState.ts", true)]
    [InlineData("wwwroot/js/lib/urlState.js", true)]
    [InlineData("../secrets.txt", false)]
    [InlineData("Domain/../Program.cs", false)]
    [InlineData("appsettings.Development.json", false)]
    [InlineData("Domain/", false)]
    [InlineData("", false)]
    public void Only_a_plain_path_under_an_allowed_root_may_be_read(string path, bool allowed)
    {
        Assert.Equal(allowed, LiveSamples.IsAllowedPath(path));
    }

    [Fact]
    public void A_region_is_cut_between_its_markers_and_nested_regions_are_kept_whole()
    {
        string[] source =
        [
            "a",
            "// #region outer",
            "b",
            "// #region inner",
            "c",
            "// #endregion",
            "d",
            "// #endregion",
            "e",
        ];
        string[] expected1 = ["b", "// #region inner", "c", "// #endregion", "d"];
        Assert.Equal(expected1, LiveSamples.Region(source, "outer"));
        string[] expected2 = ["c"];
        Assert.Equal(expected2, LiveSamples.Region(source, "inner"));
        Assert.Empty(LiveSamples.Region(source, "absent"));
    }

    [Fact]
    public void A_fence_expands_to_the_region_and_a_bad_one_to_a_note()
    {
        string expanded = LiveSamples.Expand("before\n```live path=Domain/HomePath.cs region=guard\n```\nafter", ProjectFolder.Root());
        Assert.Contains("```csharp Domain/HomePath.cs", expanded, StringComparison.Ordinal);
        Assert.Contains("public string Resolve(string? relative)", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("#region guard", expanded, StringComparison.Ordinal);
        Assert.StartsWith("before\n", expanded, StringComparison.Ordinal);
        Assert.EndsWith("\nafter", expanded, StringComparison.Ordinal);
        string note = LiveSamples.Expand("```live path=../x.cs region=y\n```", ProjectFolder.Root());
        Assert.StartsWith("> Sample unavailable", note, StringComparison.Ordinal);
    }
}

/// <summary>
/// Checks how the app finds the version and commit it reports. The version is the first bulleted
/// line of the changelog, and the commit is read from the .git folder by following HEAD to its
/// branch file. When either source is missing the answer is "unknown", because a guessed value
/// would mislead anyone checking which build is running.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed class VersionReaderTests
{
    [Fact]
    public void The_first_listed_version_wins_and_a_missing_log_is_unknown()
    {
        using var home = new TempHome();
        string log = home.File("CHANGELOG.md", "# Changelog\n\nintro 9.9.9.9 in prose is not a line\n\n- 1.0.0.3 newest.\n- 1.0.0.2 older.\n");
        Assert.Equal("1.0.0.3", VersionReader.VersionFrom(log));
        Assert.Equal(VersionReader.Unknown, VersionReader.VersionFrom(Path.Combine(home.Root, "missing.md")));
    }

    [Fact]
    public void A_folder_with_no_git_reports_unknown_and_a_ref_is_followed()
    {
        using var home = new TempHome();
        Assert.Equal(VersionReader.Unknown, VersionReader.CommitFrom(home.Root));
        home.File(".git/HEAD", "ref: refs/heads/main\n");
        home.File(".git/refs/heads/main", "0123456789abcdef0123456789abcdef01234567\n");
        Assert.Equal("0123456", VersionReader.CommitFrom(home.Root));
    }
}
