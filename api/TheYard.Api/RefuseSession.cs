// What ends a session: every authenticated request's token is checked against the store the
// request is on and the account's current security stamp. Its own file because it is the one
// place a session can be refused after it was issued, and a reader asking "what ends a session"
// should find it by name.
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace TheYard.Api;

// #region session-stamp
/// <summary>
/// Refuses a session that has ended: one opened on the other store, one whose account has moved
/// its security stamp since the token was issued (after a password reset, after a sign-out on
/// any device), or one whose account is gone. Read on every request that carries a valid token,
/// from the store the request is on, which is one point read; a token is a year long, and
/// without this nothing could end one early.
///
/// <para>The other store is refused, not skipped. One process serves both stores, and its own
/// address is one origin for both, so a cookie issued on one store reaches the other; were it
/// passed through, a session ended on its own store would still open the other one with its
/// old claims.</para>
/// </summary>
public sealed class RefuseSession(CurrentBackend current, RequestAccounts accounts)
{
    /// <summary>Why the token in <paramref name="principal"/> no longer opens a session, or null when it still does.</summary>
    /// <param name="principal">The claims the token carried.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string?> HandleAsync(ClaimsPrincipal? principal, CancellationToken cancellation)
    {
        string? id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? store = principal?.FindFirstValue(TokenIssuer.StoreClaim);
        string? stamp = principal?.FindFirstValue(TokenIssuer.StampClaim);
        if (id is null)
        {
            return "the session names no account";
        }
        if (!string.Equals(store, current.Backend.Key, StringComparison.Ordinal))
        {
            return "the session belongs to the other store";
        }
        // A store that did not come up has no accounts to compare with; every endpoint that needs
        // an account refuses on its own, because RequestAccounts answers null for it too.
        if (accounts.Users is not { } users)
        {
            return null;
        }

        cancellation.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return "the account no longer exists";
        }
        return stamp is not null && string.Equals(stamp, TokenIssuer.Fingerprint(user.SecurityStamp), StringComparison.Ordinal)
            ? null
            : "the session was ended by a sign-out or a password change";
    }
}

/// <summary>
/// What the JWT reader does with a request, resolved from that request's services so the
/// refusal is handed its store and accounts by constructor: the token is read from a cookie, and
/// a valid signature is not enough until <see cref="RefuseSession"/> has said yes.
/// </summary>
public sealed class SessionEvents(RefuseSession refuse) : JwtBearerEvents
{
    /// <summary>The token arrives in a cookie rather than an Authorization header, because a page that can read its own token can leak it.</summary>
    /// <param name="context">The message being read.</param>
    public override Task MessageReceived(MessageReceivedContext context)
    {
        if (context.Request.Cookies.TryGetValue(TokenIssuer.CookieName, out string? cookie))
        {
            context.Token = cookie;
        }
        return Task.CompletedTask;
    }

    /// <summary>Fails a validated token whose session has ended, with the reason.</summary>
    /// <param name="context">The token that validated.</param>
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        if (await refuse.HandleAsync(context.Principal, context.HttpContext.RequestAborted) is { } reason)
        {
            context.Fail(reason);
        }
    }
}
// #endregion session-stamp
