using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TheYard.Tests;

/// <summary>
/// The operator's own browsing is never a person (ADR-116, the addendum on the owner's own reads): a request that
/// presents the operator's key leaves with a cookie only the signing key can make, and a request carrying it is the
/// operator's.
/// </summary>
public class OperatorReadTests(LogEndpointTests.KeyedHost host) : IClassFixture<LogEndpointTests.KeyedHost>
{
    [Fact]
    public async Task A_request_with_the_operators_key_leaves_with_the_operators_cookie_and_a_stranger_gets_none()
    {
        var client = host.CreateClient();
        using var keyed = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        keyed.Headers.Add("X-Admin-Key", LogEndpointTests.KeyedHost.Key);

        var mine = await client.SendAsync(keyed);
        var theirs = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        string cookie = Assert.Single(mine.Headers.GetValues("Set-Cookie"), value => value.StartsWith(Hits.OperatorCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.False(theirs.Headers.TryGetValues("Set-Cookie", out var none) && none.Any(value => value.StartsWith(Hits.OperatorCookie, StringComparison.Ordinal)));
    }

    [Fact]
    public void A_request_is_the_operators_by_the_key_or_the_cookie_and_never_by_a_made_up_cookie()
    {
        var recorder = host.Services.GetRequiredService<RequestRecorder>();
        var tokens = host.Services.GetRequiredService<VisitorTokens>();

        Assert.True(recorder.IsOperator(With(Hits.OperatorCookie + "=" + tokens.OperatorMark())));
        Assert.False(recorder.IsOperator(With(Hits.OperatorCookie + "=0123456789abcdef0123456789abcdef")));
        Assert.False(recorder.IsOperator(With(null)));
        var keyed = new DefaultHttpContext().Request;
        keyed.Headers["X-Admin-Key"] = LogEndpointTests.KeyedHost.Key;
        Assert.True(recorder.IsOperator(keyed));
    }

    private static HttpRequest With(string? cookie)
    {
        var request = new DefaultHttpContext().Request;
        if (cookie is not null)
        {
            request.Headers.Cookie = cookie;
        }
        return request;
    }
}
