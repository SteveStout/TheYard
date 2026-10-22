using System.Net;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The one shape every failure answers (ADR-030): RFC 9457 ProblemDetails, with a title and a
/// sentence in detail, and never a public cache header. Every status test asserts a 400, a 401
/// and a 404 through this, so a body, a content type or a header that drifts is caught where the
/// status is.
/// </summary>
internal static class Problem
{
    /// <summary>Asserts the status, the problem content type, a title and a detail, and no public cache header.</summary>
    /// <param name="response">The answer.</param>
    /// <param name="status">The status it should carry.</param>
    public static async Task Is(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()), "a problem should have a title");
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("detail").GetString()), "a problem should say why in detail");
        Assert.NotEqual(true, response.Headers.CacheControl?.Public);
    }
}
