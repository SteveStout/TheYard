// The account handlers that open, close and read a session: register, sign in, sign out and
// who am I. Its own file because these four share the session cookie and nothing else here
// does; the routes are mapped in AccountEndpoints.cs.

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>The sign-in handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>
    /// Creates an account on this request's store and signs it in, within the hour's registration allowance.
    /// </summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Register(IServiceProvider services, CurrentBackend current, TokenIssuer issuer, RegistrationLimit limit, HttpContext http, Credentials request)
    {
        // The store this request is on keeps no accounts: the relational fallback
        // (ADR: The relational store) or a document store that did not come up.
        // Asked before UserManager is, because UserManager's store is built from
        // this same answer and would throw where this returns a sentence.
        if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
        {
            return Accounts.Unavailable();
        }
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.Problem(
                detail: "An email address and a password, please.",
                statusCode: 400, title: "The registration could not be read");
        }

        // Checked after the request is read and before the password is hashed, so a
        // request that was never going to work does not spend the hour's allowance
        // and a request that is refused does not spend the CPU. The reply says what
        // happened and how long it lasts, and says nothing about how many accounts
        // exist or how much of the allowance is left.
        if (!limit.TryTake())
        {
            return TypedResults.Problem(
                detail: "This demo is not taking new accounts at the moment. Try again in an hour.",
                statusCode: 429, title: "Too many registrations");
        }

        var user = new YardUser
        {
            UserName = request.Email,
            Email = request.Email,
            CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Nothing was made, so nothing was spent: the slot goes back, or a
            // password Identity refuses would count against strangers who never
            // registered (ADR: The one write a stranger can make, addendum).
            limit.GiveBack();
            return TypedResults.Problem(
                detail: Accounts.Explain(created),
                statusCode: 400, title: "The account was not created");
        }

        http.Response.Cookies.Append(
            TokenIssuer.CookieName,
            issuer.Issue(user.Id, user.Email!, current.Backend.Key),
            TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Describe(user));
    }

    /// <summary>
    /// Signs in with an email address and a password, answering one sentence for every way that can fail.
    /// </summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Login(IServiceProvider services, CurrentBackend current, TokenIssuer issuer, HttpContext http, Credentials request)
    {
        if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
        {
            return Accounts.Unavailable();
        }

        var user = request.Email is null ? null : await users.FindByEmailAsync(request.Email);
        // One message for "no such account" and for "wrong password", because two
        // messages are an endpoint that tells a stranger which email addresses are
        // registered here.
        // One sentence for every way this can fail, including a locked account. See
        // the lockout options: a reply that distinguishes them is a reply that tells
        // a stranger which addresses are registered here.
        var refused = TypedResults.Problem(
            detail: "That email address and password do not match an account.",
            statusCode: 401, title: "Not signed in");

        if (user is null || request.Password is null)
        {
            return refused;
        }

        // Asked before the password is checked, so a locked account does not keep
        // answering guesses, and asked through UserManager rather than by reading
        // the column, because that is what knows the window has expired.
        if (await users.IsLockedOutAsync(user))
        {
            return refused;
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            // The count is the whole mechanism. CheckPasswordAsync on its own does
            // not touch it, which is why this endpoint had a lockout policy on paper
            // and none in practice for as long as it has existed.
            await users.AccessFailedAsync(user);
            return refused;
        }

        // A success clears the count, or five wrong guesses spread over a week would
        // eventually lock somebody out of their own account.
        await users.ResetAccessFailedCountAsync(user);

        http.Response.Cookies.Append(
            TokenIssuer.CookieName,
            issuer.Issue(user.Id, user.Email!, current.Backend.Key),
            TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Describe(user));
    }

    /// <summary>Signs out by deleting the session cookie, and answers the anonymous account.</summary>
    private static Ok<AccountView> Logout(TokenIssuer issuer, HttpContext http)
    {
        // Deleted with the same attributes it was set with, or the browser keeps a
        // second cookie of the same name on a different path and stays signed in.
        http.Response.Cookies.Delete(TokenIssuer.CookieName, TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Anonymous);
    }

    /// <summary>
    /// Answers the account the session belongs to on this store, or the anonymous account when there is none here.
    /// </summary>
    private static async Task<Ok<AccountView>> WhoAmI(IServiceProvider services, CurrentBackend current, HttpContext http)
    {
        // An account is a row or a document in one store, so a session opened on
        // the other store reads as signed out here, and signs back in when the
        // toggle goes back. The page says so beside the toggle.
        if (http.UserIdOrNull() is not { } id
            || !current.Backend.Ready
            || services.GetService<UserManager<YardUser>>() is not { } users
            || await users.FindByIdAsync(id) is not { } user)
        {
            return TypedResults.Ok(Accounts.Anonymous);
        }
        return TypedResults.Ok(Accounts.Describe(user));
    }
}
