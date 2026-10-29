using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TestProject.Application;
using TestProject.Controllers;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// The three things that keep a stranger from emptying or filling the public site: writes off
/// unless a setting turns them on, a limit on writes per visitor per minute, and a byte quota
/// on the home. The first two run against the real host; the quota against the rules over the
/// in-memory store.
/// </summary>
public sealed class WriteLimitsTests : IDisposable
{
    private readonly TempHome _home = new();
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public WriteLimitsTests()
    {
        _home.File("docs/readme.md", "# hello");
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
        _home.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A client over the real host in Production, with the settings given and nothing replaced.</summary>
    private HttpClient Client(params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Files:Home", _home.Root);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
        _factories.Add(factory);
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    // #region writes-off
    [Fact]
    public async Task Every_write_answers_403_with_a_problem_document_when_writes_are_off()
    {
        using var client = Client();
        using var upload = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.bin" } };
        HttpResponseMessage[] answers =
        [
            await client.PostAsync("/api/files/upload?path=docs", upload),
            await client.PostAsync("/api/files/folder?path=docs&name=new", null),
            await client.DeleteAsync("/api/files?path=docs/readme.md"),
            await client.PostAsJsonAsync("/api/files/move", new { from = "docs/readme.md", to = "moved.md" }),
            await client.PostAsJsonAsync("/api/files/copy", new { from = "docs/readme.md", to = "copied.md" }),
        ];

        foreach (var answer in answers)
        {
            Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
            Assert.Equal("application/problem+json", answer.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
            Assert.Equal(WritesAllowedAttribute.OffReason, problem.RootElement.GetProperty("detail").GetString());
        }
        // Nothing moved, nothing was deleted, and reads still work.
        Assert.True(File.Exists(Path.Combine(_home.Root, "docs", "readme.md")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/files?path=docs")).StatusCode);
    }

    [Fact]
    public async Task Writes_work_when_the_setting_turns_them_on()
    {
        using var client = Client(("Files:AllowWrites", "true"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/files/folder?path=docs&name=new", null)).StatusCode);
    }
    // #endregion writes-off

    // #region write-limiter
    [Fact]
    public async Task A_visitor_past_the_limit_gets_429_and_another_visitor_does_not()
    {
        using var client = Client(("Files:AllowWrites", "true"), ("Files:WritesPerMinute", "2"));

        async Task<HttpResponseMessage> NewFolder(string name, string visitor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/files/folder?path=docs&name={name}");
            request.Headers.Add("X-Forwarded-For", visitor);
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Created, (await NewFolder("one", "203.0.113.7")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await NewFolder("two", "203.0.113.7")).StatusCode);
        var third = await NewFolder("three", "203.0.113.7");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Created, (await NewFolder("four", "198.51.100.9")).StatusCode);
        // Reads are never counted.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/files?path=docs")).StatusCode);
    }

    [Fact]
    public void The_visitor_is_the_last_address_the_front_end_appended()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 203.0.113.7";
        Assert.Equal("203.0.113.7", TestProject.Composition.WriteLimitRegistration.VisitorOf(context));
    }
    // #endregion write-limiter

    // #region home-quota
    [Fact]
    public async Task An_upload_or_a_copy_past_the_quota_is_refused_with_507()
    {
        string root = OperatingSystem.IsWindows() ? @"C:\home" : "/home";
        var store = new FakeStore(root);
        store.Folder("docs");
        store.File("docs/readme.md", 900);
        var options = new FilesOptions { HomeQuotaBytes = 1000 };
        var home = new HomePath(root);
        var browser = new FileBrowser(home, store, options, new HomeQuota(home, store, options));

        using var small = new MemoryStream(new byte[50]);
        await browser.UploadAsync("docs", "small.bin", 50, small, overwrite: false, CancellationToken.None);

        using var big = new MemoryStream(new byte[200]);
        var upload = await Assert.ThrowsAsync<ApiRefusalException>(() => browser.UploadAsync("docs", "big.bin", 200, big, overwrite: false, CancellationToken.None));
        var copy = Assert.Throws<ApiRefusalException>(() => browser.Copy("docs/readme.md", "again.md"));

        Assert.Equal(507, upload.Status);
        Assert.Equal(507, copy.Status);
        Assert.Equal(TestProject.Data.EntryKind.None, store.KindOf(Path.Combine(root, "again.md")));
    }
    // #endregion home-quota
}
