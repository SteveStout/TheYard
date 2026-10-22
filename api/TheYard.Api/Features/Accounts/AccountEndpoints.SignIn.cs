// The account handlers that open, close and read a session: register, sign in, sign out and
// who am I. Its own file because these four share the session cookie and nothing else here
// does; the routes are mapped in AccountEndpoints.cs. Each asks one handler in the Application
// ring's Features/Accounts and writes its answer, the cookie included.

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Accounts;

namespace TheYard.Api.Features.Accounts;

/// <summary>The sign-in handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>Creates an account on this request's store and signs it in, within the hour's registration allowance.</summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Register(RegisterHandler handler, CurrentBackend current, TokenIssuer issuer, HttpContext http, Credentials request, CancellationToken cancellation)
    {
        var answer = await handler.HandleAsync(request.Email, request.Password, cancellation);
        // The reply says what happened and how long it lasts, and says nothing about how many
        // accounts exist or how much of the allowance is left.
        return answer.Kind switch
        {
            AccountAnswerKind.Unavailable => Accounts.Unavailable(),
            AccountAnswerKind.Unreadable => TypedResults.Problem(
                detail: "An email address and a password, please.",
                statusCode: 400, title: "The registration could not be read"),
            AccountAnswerKind.TooMany => TypedResults.Problem(
                detail: "This demo is not taking new accounts at the moment. Try again in an hour.",
                statusCode: 429, title: "Too many registrations"),
            AccountAnswerKind.Refused => TypedResults.Problem(
                detail: Accounts.Explain(answer.Problem),
                statusCode: 400, title: "The account was not created"),
            _ => SignedIn(answer.Account!, current, issuer, http),
        };
    }

    /// <summary>Signs in with an email address and a password, answering one sentence for every way that can fail.</summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Login(SignInHandler handler, CurrentBackend current, TokenIssuer issuer, HttpContext http, Credentials request, CancellationToken cancellation)
    {
        var answer = await handler.HandleAsync(request.Email, request.Password, cancellation);
        return answer.Kind switch
        {
            AccountAnswerKind.Unavailable => Accounts.Unavailable(),
            // One sentence for every way this can fail: no such account, a wrong password, or a
            // locked account. A reply that told them apart would tell a stranger which email
            // addresses are registered here.
            AccountAnswerKind.Refused => TypedResults.Problem(
                detail: "That email address and password do not match an account.",
                statusCode: 401, title: "Not signed in"),
            _ => SignedIn(answer.Account!, current, issuer, http),
        };
    }

    /// <summary>Signs out: every session the account had open is refused on its next request, the cookie is deleted, and the anonymous account answers.</summary>
    private static async Task<Ok<AccountView>> Logout(SignOutHandler handler, TokenIssuer issuer, HttpContext http, CancellationToken cancellation)
    {
        await handler.HandleAsync(http.UserIdOrNull(), cancellation);
        // Deleted with the same attributes it was set with, or the browser keeps a
        // second cookie of the same name on a different path and stays signed in.
        http.Response.Cookies.Delete(TokenIssuer.CookieName, TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Anonymous);
    }

    /// <summary>The account the session belongs to on this store, or the anonymous account when there is none here.</summary>
    private static async Task<Ok<AccountView>> WhoAmI(WhoAmIHandler handler, HttpContext http, CancellationToken cancellation) =>
        TypedResults.Ok(await handler.HandleAsync(http.UserIdOrNull(), cancellation) is { } account
            ? Accounts.Describe(account)
            : Accounts.Anonymous);

    /// <summary>Sets the session cookie for the account on this request's store and answers the account.</summary>
    private static Ok<AccountView> SignedIn(Account account, CurrentBackend current, TokenIssuer issuer, HttpContext http)
    {
        http.Response.Cookies.Append(
            TokenIssuer.CookieName,
            issuer.Issue(account.Id, account.Email, current.Key, account.SecurityStamp),
            TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Describe(account));
    }
}
