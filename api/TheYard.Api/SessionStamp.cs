// The check that makes a session revocable: every authenticated request's token is compared with
// the account's current security stamp. Its own file because it is the one place a session can be
// refused after it was issued, and a reader asking "what ends a session" should find it by name.
using System.Security.Claims;

namespace TheYard.Api;

// #region session-stamp
/// <summary>
/// Refuses a session whose account has moved its security stamp since the token was issued:
/// after a password reset, after a sign-out on any device, or when the account is gone. Read on
/// every request that carries a valid token, from the store the account lives in, which is one
/// point read; a token is a year long, and without this nothing could end one early.
/// </summary>
public static class SessionStamp
{
    /// <summary>
    /// Why the token in <paramref name="principal"/> no longer opens a session, or null when it
    /// still does. A token opened on the other store is left to the endpoints, which already
    /// refuse it, because this request's store has no account to compare it with.
    /// </summary>
    /// <param name="http">The request, for its store and its accounts.</param>
    /// <param name="principal">The claims the token carried.</param>
    public static async Task<string?> WhyRefusedAsync(HttpContext http, ClaimsPrincipal? principal)
    {
        string? id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        string? store = principal?.FindFirstValue(TokenIssuer.StoreClaim);
        string? stamp = principal?.FindFirstValue(TokenIssuer.StampClaim);
        if (id is null)
        {
            return "the session names no account";
        }

        var current = http.RequestServices.GetRequiredService<CurrentBackend>();
        if (!string.Equals(store, current.Backend.Key, StringComparison.Ordinal)
            || http.RequestServices.GetRequiredService<RequestAccounts>().Users is not { } users)
        {
            return null;
        }

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
// #endregion session-stamp
