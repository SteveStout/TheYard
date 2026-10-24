// The password reset's three routes: the operator's link, the emailed link, and the page that
// uses a link to choose a new password. They read the request and write the answer; the steps of
// the reset itself are the handlers' (TheYard.Application/Features/Accounts/ResetHandlers.cs).

using Microsoft.AspNetCore.Http.HttpResults;

namespace TheYard.Api.Features.Accounts;

/// <summary>The password reset handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>
    /// Mints a reset link for an address on this site's store, behind the operator's key, and answers it for the
    /// operator to hand over.
    /// </summary>
    private static async Task<IResult> OperatorResetLink(ResetLinkRequest request, HttpContext http, OperatorResetLinkHandler handler, CurrentBackend current, AdminKey adminKey, SiteHosts hosts, CancellationToken cancellation)
    {
        if (!adminKey.IsPresentedBy(http.Request))
        {
            return Results.NotFound();
        }
        var answer = await handler.HandleAsync(new OperatorResetLinkCommand(request.Email, current.Key, hosts.LinkOrigin(http)), cancellation);
        return answer.Kind switch
        {
            AccountAnswerKind.Unavailable => Accounts.Unavailable(),
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
    private static async Task<Results<Ok<ForgotReply>, ProblemHttpResult>> Forgot(ResetLinkRequest request, HttpContext http, ForgotHandler handler, CurrentBackend current, SiteHosts hosts, CancellationToken cancellation)
    {
        var answer = await handler.HandleAsync(new ForgotCommand(request.Email, current.Key, hosts.LinkOrigin(http)), cancellation);
        return answer.Kind switch
        {
            ForgotAnswerKind.NoMail => TypedResults.Problem(
                detail: "This site cannot send email: " + answer.Reason + ".",
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "No email from here"),
            ForgotAnswerKind.Unavailable => Accounts.Unavailable(),
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
        var answer = await handler.HandleAsync(new ResetPasswordCommand(request.Token, request.Password, current.Key), cancellation);
        return answer.Kind switch
        {
            ResetAnswerKind.Unavailable => Accounts.Unavailable(),
            ResetAnswerKind.LinkRefused => TypedResults.Problem(detail: answer.Detail, statusCode: 400, title: "The link did not work"),
            ResetAnswerKind.PasswordRefused => TypedResults.Problem(
                detail: answer.Detail ?? Accounts.Explain(answer.Problem),
                statusCode: 400, title: "The password was not changed"),
            _ => SignedIn(answer.Account!, current, issuer, http),
        };
    }
}
