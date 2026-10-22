using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;
using TheYard.Application.Features.Blog;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The blog's routes through the host: every status each declares, the operator's key on the one write
/// path, a read and an event kept, and the Admin card's numbers. A second host whose store cannot keep
/// anything answers 503 where the routes say they will.
/// </summary>
public sealed class BlogEndpointsTests(BlogEndpointsTests.KeyedHost host) : IClassFixture<BlogEndpointsTests.KeyedHost>
{
    /// <summary>The host with the operator's key set.</summary>
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        /// <summary>The operator's key on this host.</summary>
        public const string Key = "the-blog-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Admin:Key", Key);
            builder.ConfigureTestServices(services => services.AddSingleton<IBlogStore>(new MemoryBlogStore("in memory, for the test")));
        }
    }

    private static object APost(string slug) => new
    {
        slug,
        title = "A post on the blog",
        summary = "Two lines about it.",
        body = "The text.\n#dotnet",
        published_at = "2026-10-19T14:00:00Z",
        linkedin_url = $"https://www.linkedin.com/posts/{slug}",
        code_url = "https://github.com/SteveStout/TheYard",
        images = new[] { new { position = 1, url = "/blog-images/a.png", alt_text = "the first slide", width = 1080, height = 1350 } },
        tags = new[] { "dotnet" },
    };

    private async Task<HttpResponseMessage> Save(object body, string? key = KeyedHost.Key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/blog/posts") { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add(AdminKey.HeaderName, key);
        }
        return await host.CreateClient().SendAsync(request);
    }

    [Fact]
    [Covers("POST", "/api/admin/blog/posts", 401)]
    [Covers("POST", "/api/admin/blog/posts", 400)]
    [Covers("GET", "/api/blog/posts/{slug}", 404)]
    [Covers("POST", "/api/blog/reads", 400)]
    [Covers("POST", "/api/blog/reads", 404)]
    [Covers("POST", "/api/blog/events", 400)]
    public async Task Each_route_answers_the_statuses_it_declares()
    {
        await Problem.Is(await Save(APost("a-keyed-post"), key: null), HttpStatusCode.Unauthorized);
        await Problem.Is(await Save(APost("Not A Slug")), HttpStatusCode.BadRequest);
        var client = host.CreateClient();
        await Problem.Is(await client.GetAsync("/api/blog/posts/no-such-post"), HttpStatusCode.NotFound);
        await Problem.Is(await client.PostAsJsonAsync("/api/blog/reads", new { referrer = "https://www.linkedin.com/" }), HttpStatusCode.BadRequest);
        await Problem.Is(await client.PostAsJsonAsync("/api/blog/reads", new { slug = "no-such-post" }), HttpStatusCode.NotFound);
        await Problem.Is(await client.PostAsJsonAsync("/api/blog/events", new { slug = "no-such-post", type = "hover", value = "1" }), HttpStatusCode.BadRequest);
    }

    [Fact]
    [Covers("POST", "/api/admin/blog/posts", 200)]
    [Covers("GET", "/api/blog/posts", 200)]
    [Covers("GET", "/api/blog/posts/{slug}", 200)]
    [Covers("POST", "/api/blog/reads", 200)]
    [Covers("POST", "/api/blog/events", 204)]
    [Covers("GET", "/api/admin/blog", 200)]
    public async Task A_post_saved_with_the_key_is_listed_read_logged_and_counted()
    {
        string slug = "post-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.OK, (await Save(APost(slug))).StatusCode);
        var client = host.CreateClient();
        using var list = JsonDocument.Parse(await client.GetStringAsync("/api/blog/posts"));
        Assert.Contains(list.RootElement.GetProperty("posts").EnumerateArray(), p => p.GetProperty("slug").GetString() == slug);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/blog/posts/{slug}")).StatusCode);
        var read = await client.PostAsJsonAsync("/api/blog/reads", new { slug, referrer = "https://www.linkedin.com/feed/" });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Contains("\"source\":\"linkedin\"", await read.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/blog/events", new { slug, type = "depth", value = "50" })).StatusCode);
        var statsAnswer = await client.GetAsync("/api/admin/blog");
        Assert.Equal(HttpStatusCode.OK, statsAnswer.StatusCode);
        using var stats = JsonDocument.Parse(await statsAnswer.Content.ReadAsStringAsync());
        Assert.True(stats.RootElement.GetProperty("available").GetBoolean());
    }
}

/// <summary>The blog's routes on a host whose store cannot keep anything: 503 with the store's own reason.</summary>
public sealed class BlogUnkeptTests(BlogUnkeptTests.UnkeptHost host) : IClassFixture<BlogUnkeptTests.UnkeptHost>
{
    /// <summary>The host with the operator's key set and a store that cannot keep anything.</summary>
    public sealed class UnkeptHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Admin:Key", BlogEndpointsTests.KeyedHost.Key);
            builder.ConfigureTestServices(services => services.AddSingleton<IBlogStore>(new Unkept()));
        }
    }

    [Fact]
    [Covers("GET", "/api/blog/posts", 503)]
    [Covers("POST", "/api/admin/blog/posts", 503)]
    public async Task A_store_that_cannot_keep_answers_503_with_its_reason()
    {
        var client = host.CreateClient();
        await Problem.Is(await client.GetAsync("/api/blog/posts"), HttpStatusCode.ServiceUnavailable);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/blog/posts")
        {
            Content = JsonContent.Create(new { slug = "a-post", title = "T", summary = "S", body = "B", published_at = "2026-10-19T14:00:00Z", linkedin_url = "https://www.linkedin.com/posts/a" }),
        };
        request.Headers.Add(AdminKey.HeaderName, BlogEndpointsTests.KeyedHost.Key);
        await Problem.Is(await client.SendAsync(request), HttpStatusCode.ServiceUnavailable);
    }

    private sealed class Unkept : IBlogStore
    {
        public Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new BlogAvailability(false, "the blog schema is not published here, for the test"));

        public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation) => throw new InvalidOperationException("not kept");

        public Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation) => Task.FromResult<BlogPost?>(null);

        public Task<int> SaveAsync(BlogPost post, DateTimeOffset at, CancellationToken cancellation) => throw new InvalidOperationException("not kept");

        public Task KeepReadAsync(BlogRead read, CancellationToken cancellation) => throw new InvalidOperationException("not kept");

        public Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation) => throw new InvalidOperationException("not kept");

        public Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, CancellationToken cancellation) => throw new InvalidOperationException("not kept");

        public Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation) => throw new InvalidOperationException("not kept");
    }
}
