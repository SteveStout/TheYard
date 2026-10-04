// The password reset's three handlers: the operator's link, the emailed link, and the page that
// uses a link to choose a new password. They read the request and write the answer; the steps of
// the reset itself are PasswordReset's (TheYard.Api/PasswordReset.cs).

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The password reset handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>
    /// Mints a reset link for an address on this site's store, behind the operator's key, and answers it for the
    /// operator to hand over.
    /// </summary>
    private static async Task<IResult> OperatorResetLink(ResetLinkRequest request, HttpContext http, RequestAccounts accounts, CurrentBackend current, PasswordReset reset, AdminKey adminKey, IConfiguration configuration, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.NotFound();
        }
        if (accounts.Users is not { } users)
        {
            return Accounts.Unavailable();
        }

        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Results.Problem(
                detail: "No account with that email address on this site's store.",
                statusCode: 404, title: "No such account");
        }

        string url = await reset.LinkForAsync(user, current.Backend.Key, SiteOrigin(http, configuration), cancellation);
        return Results.Json(new
        {
            email = user.Email,
            store = current.Backend.Key,
            url,
            expires_at = Clocks.UtcNow() + TokenIssuer.ResetLifetime,
        });
    }

    /// <summary>
    /// Emails a reset link to an address, answering the same sentence whether or not the address has an account
    /// here.
    /// </summary>
    private static async Task<Results<Ok<ForgotReply>, ProblemHttpResult>> Forgot(ResetLinkRequest request, HttpContext http, RequestAccounts accounts, CurrentBackend current, PasswordReset reset, IEmailSender mail, ForgotLimit limit, IConfiguration configuration, CancellationToken cancellation)
    {
        if (!mail.Configured)
        {
            return TypedResults.Problem(
                detail: "This site cannot send email: " + mail.Reason + ".",
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "No email from here");
        }
        if (accounts.Users is not { } users)
        {
            return Accounts.Unavailable();
        }
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return TypedResults.Problem(detail: "An email address is needed.", statusCode: 400, title: "Nothing to send to");
        }

        var sentence = new ForgotReply(true, "If that address has an account here, a reset link is on its way. It works once, for an hour.");
        if (!limit.TryTake(request.Email))
        {
            return TypedResults.Ok(sentence);
        }

        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null && user.Email is not null)
        {
            string url = await reset.LinkForAsync(user, current.Backend.Key, SiteOrigin(http, configuration), cancellation);
            await mail.SendAsync(
                user.Email,
                "Your TheYard password reset link",
                "Somebody asked to reset the password for this address on TheYard. If it was you, open this link within the hour; it works once:\n\n"
                + url
                + "\n\nIf it was not you, nothing has changed and you can ignore this message.",
                cancellation);
        }

        return TypedResults.Ok(sentence);
    }

    /// <summary>
    /// Uses a reset link to choose a new password and sign in; the link works once, for an hour, on the site it was
    /// minted for. The steps are PasswordReset's; this reads the request and writes the answer.
    /// </summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Reset(ResetRequest request, HttpContext http, RequestAccounts accounts, CurrentBackend current, PasswordReset reset, TokenIssuer issuer, CancellationToken cancellation)
    {
        if (accounts.Users is not { } users)
        {
            return Accounts.Unavailable();
        }

        var result = await reset.ResetAsync(users, request.Token, request.Password, current.Backend.Key, cancellation);
        if (result.User is not { } user)
        {
            return TypedResults.Problem(detail: result.Detail, statusCode: 400, title: result.Title);
        }

        http.Response.Cookies.Append(
            TokenIssuer.CookieName,
            issuer.Issue(user.Id, user.Email!, current.Backend.Key),
            TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Describe(user));
    }

    /// <summary>
    /// This site as a visitor reaches it, for a link: the site's configured address first (Site:Url,
    /// the domain behind the edge, because the request's own host there is the origin server), then
    /// the forwarded host, then the request's host.
    /// </summary>
    private static string SiteOrigin(HttpContext http, IConfiguration configuration)
    {
        string? siteUrl = configuration["Site:Url"];
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            return siteUrl.TrimEnd('/');
        }
        string scheme = http.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Request.Scheme;
        string host = http.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Request.Host.Value ?? "";
        return $"{scheme}://{host}";
    }
}
