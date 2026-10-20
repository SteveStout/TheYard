// The password reset's three routes: the operator's link, the emailed link, and the page that
// uses a link to choose a new password. They read the request and write the answer; the steps of
// the reset itself are the handlers' (TheYard.Application/Features/Accounts/ResetHandlers.cs).

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Accounts;

namespace TheYard.Api.Features.Accounts;

/// <summary>The password reset handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>
    /// Mints a reset link for an address on this site's store, behind the operator's key, and answers it for the
    /// operator to hand over.
    /// </summary>
    private static async Task<IResult> OperatorResetLink(ResetLinkRequest request, HttpContext http, OperatorResetLinkHandler handler, CurrentBackend current, AdminKey adminKey, IConfiguration configuration, SiteHosts hosts, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.NotFound();
        }
        var answer = await handler.HandleAsync(request.Email, current.Key, SiteOrigin(http, configuration, hosts), cancellation);
        return answer.Kind switch
        {
            AccountAnswerKind.Unavailable => TheYard.Api.Accounts.Unavailable(),
            AccountAnswerKind.Refused => Results.Problem(
                detail: "No account with that email address on this site's store.",
                statusCode: 404, title: "No such account"),
            _ => Results.Json(new
            {
                email = answer.Account!.Email,
                store = current.Key,
                url = answer.Url,
                expires_at = Clocks.UtcNow() + TokenIssuer.ResetLifetime,
            }),
        };
    }

    /// <summary>
    /// Emails a reset link to an address, answering the same sentence whether or not the address has an account
    /// here.
    /// </summary>
    private static async Task<Results<Ok<ForgotReply>, ProblemHttpResult>> Forgot(ResetLinkRequest request, HttpContext http, ForgotHandler handler, CurrentBackend current, IConfiguration configuration, SiteHosts hosts, CancellationToken cancellation)
    {
        var answer = await handler.HandleAsync(request.Email, current.Key, SiteOrigin(http, configuration, hosts), cancellation);
        return answer.Kind switch
        {
            ForgotAnswerKind.NoMail => TypedResults.Problem(
                detail: "This site cannot send email: " + answer.Reason + ".",
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "No email from here"),
            ForgotAnswerKind.Unavailable => TheYard.Api.Accounts.Unavailable(),
            ForgotAnswerKind.NoAddress => TypedResults.Problem(detail: "An email address is needed.", statusCode: 400, title: "Nothing to send to"),
            _ => TypedResults.Ok(new ForgotReply(true, "If that address has an account here, a reset link is on its way. It works once, for an hour.")),
        };
    }

    /// <summary>
    /// Uses a reset link to choose a new password and sign in; the link works once, for an hour, on the site it was
    /// minted for. The steps are the handler's; this reads the request and writes the answer.
    /// </summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Reset(ResetRequest request, HttpContext http, ResetPasswordHandler handler, CurrentBackend current, TokenIssuer issuer, CancellationToken cancellation)
    {
        var answer = await handler.HandleAsync(request.Token, request.Password, current.Key, cancellation);
        return answer.Kind switch
        {
            ResetAnswerKind.Unavailable => TheYard.Api.Accounts.Unavailable(),
            ResetAnswerKind.LinkRefused => TypedResults.Problem(detail: answer.Detail, statusCode: 400, title: "The link did not work"),
            ResetAnswerKind.PasswordRefused => TypedResults.Problem(
                detail: answer.Detail ?? TheYard.Api.Accounts.Explain(answer.Problem),
                statusCode: 400, title: "The password was not changed"),
            _ => SignedIn(answer.Account!, current, issuer, http),
        };
    }

    /// <summary>
    /// This site as a visitor reaches it, for a link: the Yard name the request arrived on when one
    /// web app serves both sites (SiteHosts, which takes only a name configured here, so a sender can
    /// choose between the two sites and nothing else), then the configured address (Site:Url, the
    /// domain behind the edge, because the request's own host there is the origin server), or on a
    /// machine with neither, the request's own scheme and host. Never X-Forwarded-Host: a header is
    /// what the sender says it is, and a link in a reset email must not be one a sender chose (ADR: The
    /// order of the request pipeline). Every web app the template writes carries Site:Url.
    /// </summary>
    private static string SiteOrigin(HttpContext http, IConfiguration configuration, SiteHosts hosts)
    {
        if (hosts.SiteOrigin(http) is { } arrivedOn)
        {
            return arrivedOn;
        }
        string? siteUrl = configuration["Site:Url"];
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            return siteUrl.TrimEnd('/');
        }
        return $"{http.Request.Scheme}://{http.Request.Host.Value}";
    }
}
