using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The "Where next" footer (ladder v7, page flow): one list in data/hooks.json, drawn by the React pages and by the two
/// pages the API draws in C#. These hold the C# side to the file, every footer to two links that never open Admin, and
/// every drawn page to never carrying the email address: the contact line is the recorded redirect /api/go/email.
/// </summary>
public sealed class WhereNextTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static JsonElement File() => JsonDocument.Parse(System.IO.File.ReadAllText(Repo.DataFile("hooks.json"))).RootElement.Clone();

    private static string[] Hrefs(string page) =>
        [.. File().GetProperty("next").EnumerateArray().Single(entry => entry.GetProperty("page").GetString() == page)
            .GetProperty("links").EnumerateArray().Select(link => link.GetProperty("href").GetString()!)];

    private static bool OpensAdmin(string href)
    {
        var address = new Uri(new Uri("https://theyard.stevenstout.biz"), href);
        return Regex.IsMatch(address.Query, @"(^|[?&])view=admin(&|$)") || address.AbsolutePath.StartsWith("/admin", StringComparison.Ordinal)
            || address.AbsolutePath.StartsWith("/api/admin", StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/about", "about")]
    [InlineData("/resume?src=email", "resume")]
    public async Task The_pages_the_api_draws_end_with_the_files_where_next_links(string address, string page)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        string html = await client.GetStringAsync(address);
        int nav = html.IndexOf($"data-next=\"{page}\"", StringComparison.Ordinal);
        Assert.True(nav >= 0, $"{address} has no Where next footer");
        string footer = html[nav..html.IndexOf("</nav>", nav, StringComparison.Ordinal)];
        string[] drawn = [.. Regex.Matches(footer, "href=\"([^\"]+)\"").Select(match => match.Groups[1].Value)];
        Assert.Equal(Hrefs(page), drawn);
    }

    [Fact]
    public void Every_footer_has_at_most_two_links_each_with_a_reason_and_minutes_and_none_opens_admin()
    {
        var root = File();
        Assert.NotEmpty(root.GetProperty("next").EnumerateArray());
        foreach (var entry in root.GetProperty("next").EnumerateArray())
        {
            var links = entry.GetProperty("links").EnumerateArray().ToList();
            Assert.InRange(links.Count, 1, 2);
            foreach (var link in links)
            {
                Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("reason").GetString()));
                Assert.InRange(link.GetProperty("minutes").GetInt32(), 1, 30);
                Assert.False(OpensAdmin(link.GetProperty("href").GetString()!), $"{entry.GetProperty("page")} links to Admin");
            }
        }

        foreach (var hook in root.GetProperty("hooks").EnumerateArray())
        {
            if (hook.GetProperty("link").ValueKind == JsonValueKind.Object)
            {
                Assert.False(OpensAdmin(hook.GetProperty("link").GetProperty("href").GetString()!), $"{hook.GetProperty("page")}'s hook links to Admin");
            }
        }
    }

    [Fact]
    public void No_drawn_page_carries_the_email_address_or_a_mailto()
    {
        string root = Repo.Root();
        var drawn = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.ts*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "render"), "*.ts", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "api", "TheYard.Api", "Features", "Docs"), "*.cs"))
            .Where(path => !path.Contains(".test.", StringComparison.Ordinal))
            .Append(Path.Combine(root, "docs", "author", "AUTHOR.md"))
            .Append(Repo.DataFile("hooks.json"));
        var carrying = drawn.Where(path => System.IO.File.ReadAllText(path) is var text
            && (text.Contains("mailto:", StringComparison.OrdinalIgnoreCase) || text.Contains("stevenstout11@", StringComparison.Ordinal)));
        Assert.Empty(carrying.Select(path => Path.GetRelativePath(root, path)));
    }
}
