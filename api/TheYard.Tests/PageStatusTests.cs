using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The page sweep (ADR: Every page, checked at every roll): the list of
/// addresses is the catalogue rather than a copy of it, and every address on
/// it answers with something.
///
/// <para>This is the test the README's two broken links needed and did not
/// have. Both answered 200 for weeks while serving raw markdown to anybody who
/// followed them, because nothing asked what an address answered with. The
/// sweep asks; this holds the sweep to the catalogue so a document cannot be
/// added without being checked.</para>
/// </summary>
public class PageStatusTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void The_sweep_checks_every_document_and_every_drawing_the_catalogue_serves()
    {
        var addresses = ServedAddresses.All(true).Select(address => address.Address).ToHashSet(StringComparer.Ordinal);

        var missing = DocsCatalog.Files.Keys
            .Select(slug => $"/api/docs/{slug}")
            .Concat(DocsCatalog.Diagrams.Keys.Select(name => $"/api/docs/diagrams/{name}"))
            .Where(address => !addresses.Contains(address))
            .ToList();

        Assert.True(missing.Count == 0, "the sweep does not check: " + string.Join(", ", missing));
        Assert.True(addresses.Contains("/"), "the sweep should check the app itself");
        // The Admin tab's readings are pages too: an endpoint that throws is a
        // page that is down, and one of them did while this sweep was green.
        foreach (string reading in new[] { "/api/admin/machines", "/api/admin/metrics", "/api/admin/pages" })
        {
            Assert.Contains(reading, addresses);
        }
        Assert.Equal(addresses.Count, ServedAddresses.All(true).Count);
    }

    /// <summary>
    /// The four addresses the frontend build puts in the web root are checked
    /// where they exist and named nowhere else: the image has them, a checkout
    /// with the dev server in front of the API does not, and a sweep that
    /// called them down on a developer's machine would be a false reading.
    /// </summary>
    [Fact]
    public void The_addresses_the_build_puts_in_the_web_root_are_checked_only_where_they_are_served()
    {
        var withFrontend = ServedAddresses.All(true).Select(address => address.Address).ToList();
        var without = ServedAddresses.All(false).Select(address => address.Address).ToList();

        Assert.Equal(ServedAddresses.FromTheBuild.Count, withFrontend.Count - without.Count);
        foreach (var address in ServedAddresses.FromTheBuild)
        {
            Assert.Contains(address.Address, withFrontend);
            Assert.DoesNotContain(address.Address, without);
        }
        // Everything else is checked either way, the documents above all.
        Assert.Contains("/api/docs/performance", without);
    }

    /// <summary>
    /// The bound address as something this container can dial. Every row here
    /// is a shape Kestrel actually reports, and the last one is the shape this
    /// sweep does not use (SelfAddress).
    /// </summary>
    [Theory]
    [InlineData("http://[::]:8080", "http://127.0.0.1:8080")]
    [InlineData("http://+:8080", "http://127.0.0.1:8080")]
    [InlineData("http://*:5000", "http://127.0.0.1:5000")]
    [InlineData("http://localhost:5210", "http://127.0.0.1:5210")]
    // Port 80 is http's own, so the address a client dials does not repeat it.
    [InlineData("http://0.0.0.0:80", "http://127.0.0.1")]
    [InlineData("https://+:443", null)]
    public void A_bound_address_becomes_one_this_container_can_dial(string bound, string? expected) =>
        Assert.Equal(expected, SelfAddress.Dialable(bound));

    [Fact]
    public async Task Every_address_the_sweep_checks_answers_with_something()
    {
        var runner = new PageStatusRunner(() => null, () => false, "test", "test");
        using var client = factory.CreateClient();

        var report = await runner.RunAsync(client, "test", CancellationToken.None);

        var down = report.Entries
            .Where(entry => !entry.Ok)
            .Select(entry => $"{entry.Address} answered {entry.Status} in {entry.Bytes} bytes{(entry.Reason is null ? "" : " (" + entry.Reason + ")")}")
            .ToList();

        Assert.True(down.Count == 0, "these addresses are down:" + Environment.NewLine + string.Join(Environment.NewLine, down));
        Assert.Equal(report.Checked, report.Up);
        Assert.True(report.Checked > 50, $"only {report.Checked} addresses were checked, which suggests the list stopped being built from the catalogue");
    }

    /// <summary>
    /// A document is markdown and a drawing is a page, which is the distinction
    /// the README got wrong. The sweep records the type it was served, so the
    /// card shows it and this can hold it.
    /// </summary>
    [Fact]
    public async Task A_document_is_served_as_markdown_and_a_drawing_as_a_page()
    {
        var runner = new PageStatusRunner(() => null, () => false, "test", "test");
        using var client = factory.CreateClient();

        var report = await runner.RunAsync(client, "test", CancellationToken.None);

        var wrong = report.Entries
            .Where(entry => entry.Kind == "document" && entry.ContentType != "text/markdown")
            .Concat(report.Entries.Where(entry => entry.Kind == "drawing" && entry.ContentType != "text/html"))
            .Select(entry => $"{entry.Address} is a {entry.Kind} and was served as {entry.ContentType}")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public async Task The_endpoint_serves_the_last_sweep_and_says_when_there_has_not_been_one()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/pages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        // The test host binds no address, so the roll sweep does not run here
        // and the endpoint says so rather than inventing a result.
        Assert.Contains("not run", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sweep is this container talking to itself, so it is not traffic: the
    /// request hook drops anything carrying the header, and the timing section
    /// shows what visitors did (ADR: Every page, checked at every roll).
    /// </summary>
    [Fact]
    public async Task A_request_carrying_the_sweep_header_is_not_counted_as_traffic()
    {
        using var client = factory.CreateClient();
        string Window() => client.GetStringAsync("/api/admin/metrics").GetAwaiter().GetResult();

        string before = Window();
        for (int i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/version");
            request.Headers.TryAddWithoutValidation(PageStatusRunner.CheckHeader, "1");
            using var swept = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, swept.StatusCode);
        }
        string after = Window();

        Assert.Equal(WindowSize(before), WindowSize(after));

        // And an ordinary request still is, so the assertion above is not
        // passing because nothing is counted at all.
        using var counted = await client.GetAsync("/api/version");
        Assert.Equal(HttpStatusCode.OK, counted.StatusCode);
        Assert.True(WindowSize(Window()) > WindowSize(after), "an ordinary request should be counted");
    }

    private static int WindowSize(string metrics)
    {
        using var document = System.Text.Json.JsonDocument.Parse(metrics);
        return document.RootElement.GetProperty("requests").GetProperty("window").GetInt32();
    }
}
