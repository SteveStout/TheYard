using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TheYard.Api;
using TheYard.Api.Features.Accounts;

namespace TheYard.Tests;

// #region refuse-session-tests
/// <summary>
/// The refusal on its own, with fake ports: a request's store and accounts handed in, no host
/// (ADR: The onion cut into vertical slices, rules 6 and 7). One process serves both stores, so a
/// token whose store is not the request's is refused rather than passed through.
/// </summary>
public sealed class RefuseSessionTests
{
    private static RefuseSession On(string store)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[Backends.HeaderName] = store;
        var backends = new Backends([FakeBackend.Named("sql", "Azure SQL Database"), FakeBackend.Named("cosmos", "Azure Cosmos DB")], "sql");
        var current = new CurrentBackend(backends, new HttpContextAccessor { HttpContext = context });
        // A store with no accounts here, so only the claims decide.
        return new RefuseSession(current, new RequestAccounts(() => null));
    }

    private static ClaimsPrincipal Token(string? id, string store)
    {
        var claims = new List<Claim> { new(TokenIssuer.StoreClaim, store), new(TokenIssuer.StampClaim, "stamp") };
        if (id is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task A_token_from_the_other_store_is_refused_and_one_from_this_store_is_left_to_its_accounts()
    {
        Assert.Equal("the session belongs to the other store", await On("cosmos").HandleAsync(Token("user-1", "sql"), CancellationToken.None));
        Assert.Equal("the session belongs to the other store", await On("sql").HandleAsync(Token("user-1", "cosmos"), CancellationToken.None));
        Assert.Null(await On("sql").HandleAsync(Token("user-1", "sql"), CancellationToken.None));
    }

    [Fact]
    public async Task A_token_that_names_no_account_is_refused()
    {
        Assert.Equal("the session names no account", await On("sql").HandleAsync(Token(null, "sql"), CancellationToken.None));
        Assert.Equal("the session names no account", await On("sql").HandleAsync(null, CancellationToken.None));
    }
}
// #endregion refuse-session-tests
