using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// /about's summary says what the resume of record says (ADR: The resume has an address): Storee's platform is
/// .NET 10, as the resume locked on 10 November reads, and the summary names no earlier .NET version for it.
/// </summary>
public sealed class AboutSummaryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task About_says_the_platform_is_on_the_net_version_the_resume_says()
    {
        using var response = await factory.CreateClient().GetAsync("/about");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("onto a single .NET 10 platform on Azure", html, StringComparison.Ordinal);
        Assert.DoesNotContain(".NET 9", html, StringComparison.Ordinal);
    }
}
