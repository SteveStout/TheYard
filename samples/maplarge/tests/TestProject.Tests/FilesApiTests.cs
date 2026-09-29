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
/// The real host booted in memory over a temporary home (ADR-004): every route,
/// the snake_case wire, and the problem document on every failure. What the
/// browser tests cannot see, this does: the disk adapter, the JSON options and
/// the exception handler working together.
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
            // Production, so the HTTPS redirect the starter had stays out of the way (ADR-001).
            builder.UseEnvironment("Production");
            builder.UseSetting("Files:Home", _home.Root);
            builder.UseSetting("Files:MaxUploadBytes", "64");
            builder.UseSetting("Files:SearchLimit", "2");
            // The home and the limits are bound from settings, so no service is
            // replaced and the wiring under test is the wiring that ships.
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
