using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TestProject.Tests;

/// <summary>
/// The Shed's pipeline where it differs from TheYard's for a reason: no edge stands in front of
/// this site, so App Service's front end is the one hop, the scheme it forwards is read, and the
/// app sends HSTS itself in production, only on a request that reached it over HTTPS.
/// </summary>
public sealed class RequestPipelineTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_request_forwarded_as_https_gets_HSTS_and_a_plain_one_does_not()
    {
        // A host of its own: HSTS is never sent to localhost, which a browser would remember
        // for every site a developer runs there.
        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://theshed.example.test") });
        using var overTls = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        overTls.Headers.Add("X-Forwarded-Proto", "https");
        HttpResponseMessage secured = await client.SendAsync(overTls);
        Assert.True(secured.Headers.Contains("Strict-Transport-Security"), "a request that reached App Service over HTTPS is told to stay on it");

        HttpResponseMessage plain = await client.GetAsync("/healthz");
        Assert.False(plain.Headers.Contains("Strict-Transport-Security"), "HSTS on plain HTTP is ignored by browsers and is not sent");
    }
}
