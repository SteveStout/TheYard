using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TestProject.Application;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Runs the real app in memory over a temporary home folder and calls every API route over HTTP.
/// It checks that the JSON the API sends uses snake_case field names, and that every failure
/// returns a problem document (application/problem+json) with a status, a detail and a trace id.
/// FileBrowserTests uses an in-memory store, so only these tests cover the class that reads and
/// writes real files, the JSON settings and the exception handler working together.
/// (more in docs/ADR-004-the-wire.md)
/// </summary>
public sealed class FilesApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly TempHome _home = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public FilesApiTests()
    {
        _home.File("docs/readme.md", "# hello");
        _home.File("docs/notes/todo.md", "- one");
        _home.File("Archive/old.zip", "zip");
        _home.File("apple.txt", "apple");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Run as Production. In Development the app redirects HTTP to HTTPS, and the test
            // client would get that redirect instead of the API's answer.
            builder.UseEnvironment("Production");
            builder.UseSetting("Files:Home", _home.Root);
            builder.UseSetting("Files:MaxUploadBytes", "64");
            builder.UseSetting("Files:SearchLimit", "2");
            // The home folder and the limits go in as ordinary settings. No service is
            // replaced, so the test runs the same setup code the deployed app runs.
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _home.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Browse_home_is_snake_case_with_totals_and_timing()
    {
        JsonElement listing = await Get("/api/files");
        Assert.Equal("", listing.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("parent").ValueKind);
        string[] expected1 = ["Archive", "docs"];
        Assert.Equal(expected1, listing.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("name").GetString()));
        string[] expected2 = ["apple.txt"];
        Assert.Equal(expected2, listing.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()));
        JsonElement totals = listing.GetProperty("totals");
        Assert.Equal(2, totals.GetProperty("folder_count").GetInt32());
        Assert.Equal(1, totals.GetProperty("file_count").GetInt32());
        Assert.Equal(5, totals.GetProperty("total_bytes").GetInt64());
        Assert.True(listing.GetProperty("took_ms").GetInt64() >= 0);
        JsonElement file = listing.GetProperty("files")[0];
        Assert.Equal("txt", file.GetProperty("extension").GetString());
        Assert.True(file.GetProperty("modified_ms").GetInt64() > 1_600_000_000_000);
    }

    [Fact]
    public async Task Browse_of_a_subfolder_names_the_parent()
    {
        JsonElement listing = await Get("/api/files?path=docs/notes");
        Assert.Equal("docs/notes", listing.GetProperty("path").GetString());
        Assert.Equal("docs", listing.GetProperty("parent").GetString());
    }

    [Fact]
    public async Task A_missing_folder_is_a_404_problem()
    {
        await AssertProblem(await _client.GetAsync("/api/files?path=nowhere"), HttpStatusCode.NotFound, "nowhere");
    }

    [Fact]
    public async Task A_path_that_climbs_out_is_a_400_problem()
    {
        await AssertProblem(await _client.GetAsync("/api/files?path=../.."), HttpStatusCode.BadRequest, "'.' or '..'");
        await AssertProblem(await _client.GetAsync("/api/files?path=%2Fetc"), HttpStatusCode.BadRequest, "relative");
    }

    [Fact]
    public async Task Search_finds_by_glob_and_reports_truncation()
    {
        JsonElement result = await Get("/api/files/search?q=*.md");
        Assert.Equal("*.md", result.GetProperty("query").GetString());
        string[] expected3 = ["docs/notes/todo.md", "docs/readme.md"];
        Assert.Equal(expected3, result.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        Assert.False(result.GetProperty("truncated").GetBoolean());
        JsonElement everything = await Get("/api/files/search?q=*&limit=1");
        Assert.True(everything.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Search_with_no_query_is_400()
    {
        await AssertProblem(await _client.GetAsync("/api/files/search?q="), HttpStatusCode.BadRequest, "look for");
    }

    [Fact]
    public async Task Upload_then_download_round_trips_the_bytes()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 250, 251, 252];
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "files", "blob.bin" } };
        HttpResponseMessage uploaded = await _client.PostAsync("/api/files/upload?path=docs", form);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        JsonElement result = await uploaded.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("docs/blob.bin", result.GetProperty("entries")[0].GetProperty("path").GetString());
        Assert.Equal(2, result.GetProperty("totals").GetProperty("file_count").GetInt32());

        HttpResponseMessage download = await _client.GetAsync("/api/files/download?path=docs/blob.bin");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("blob.bin", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("bytes", download.Headers.AcceptRanges.Single());
        Assert.NotNull(download.Content.Headers.LastModified);

        // A resumed download asks for the bytes it is missing and gets only those.
        using var resume = new HttpRequestMessage(HttpMethod.Get, "/api/files/download?path=docs/blob.bin");
        resume.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
        HttpResponseMessage part = await _client.SendAsync(resume);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(bytes[..4], await part.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_of_an_existing_name_is_409_unless_overwrite()
    {
        using var first = new MultipartFormDataContent { { new StringContent("new"), "files", "readme.md" } };
        await AssertProblem(await _client.PostAsync("/api/files/upload?path=docs", first), HttpStatusCode.Conflict, "already exists");
        using var second = new MultipartFormDataContent { { new StringContent("new"), "files", "readme.md" } };
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/files/upload?path=docs&overwrite=true", second)).StatusCode);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_home.Root, "docs", "readme.md")));
    }

    [Fact]
    public async Task Upload_past_the_limit_is_413()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[65]), "files", "big.bin" } };
        await AssertProblem(await _client.PostAsync("/api/files/upload", form), HttpStatusCode.RequestEntityTooLarge, "limit");
    }

    [Fact]
    public async Task Upload_with_no_files_is_400()
    {
        using var form = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        await AssertProblem(await _client.PostAsync("/api/files/upload", form), HttpStatusCode.BadRequest, "at least one file");
    }

    [Fact]
    public async Task Folder_create_delete_move_copy()
    {
        HttpResponseMessage created = await _client.PostAsync("/api/files/folder?path=docs&name=drafts", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(_home.Root, "docs", "drafts")));
        await AssertProblem(await _client.PostAsync("/api/files/folder?path=docs&name=drafts", null), HttpStatusCode.Conflict, "already exists");

        HttpResponseMessage moved = await _client.PostAsJsonAsync("/api/files/move", new { from = "apple.txt", to = "docs/drafts/apple.txt" }, Wire);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.False(File.Exists(Path.Combine(_home.Root, "apple.txt")));
        Assert.Equal("docs/drafts/apple.txt", (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString());

        HttpResponseMessage copied = await _client.PostAsJsonAsync("/api/files/copy", new { from = "docs", to = "docs-copy" }, Wire);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        Assert.True(File.Exists(Path.Combine(_home.Root, "docs-copy", "drafts", "apple.txt")));
        Assert.True(File.Exists(Path.Combine(_home.Root, "docs", "drafts", "apple.txt")));

        HttpResponseMessage deleted = await _client.DeleteAsync("/api/files?path=docs-copy");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_home.Root, "docs-copy")));
        await AssertProblem(await _client.DeleteAsync("/api/files?path="), HttpStatusCode.BadRequest, "cannot be deleted");
        await AssertProblem(await _client.PostAsJsonAsync("/api/files/move", new { from = "docs", to = "docs/drafts/docs" }, Wire), HttpStatusCode.BadRequest, "into itself");
    }

    [Fact]
    public async Task The_filesystem_refusing_a_delete_is_a_problem_document_not_a_500()
    {
        string locked = _home.File("Archive/keep.txt", "keep");
        // Make the folder delete fail, to check that the error becomes a 409 or 403 problem
        // document rather than an unhandled 500. On Windows a read-only file inside the folder is
        // enough. Elsewhere the test also holds the file open with no sharing, the nearest match.
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        using FileStream? hold = OperatingSystem.IsWindows() ? null : new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        HttpResponseMessage response = await _client.DeleteAsync("/api/files?path=Archive");
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return; // This OS allowed the delete anyway, so there is no refusal to check.
        }
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden, $"expected 409 or 403, got {(int)response.StatusCode}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        File.SetAttributes(locked, FileAttributes.Normal);
    }

    [Fact]
    public async Task An_unknown_api_route_is_a_problem_too()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void The_home_is_the_configured_folder()
    {
        HomePath home = _factory.Services.GetRequiredService<HomePath>();
        Assert.Equal(Path.GetFullPath(_home.Root).TrimEnd(Path.DirectorySeparatorChar), home.Root);
        Assert.Equal(64, _factory.Services.GetRequiredService<FilesOptions>().MaxUploadBytes);
    }

    private async Task<JsonElement> Get(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string detailContains)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Contains(detailContains, problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
    }
}
