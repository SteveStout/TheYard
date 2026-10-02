// The password reset in its two halves: the link (minted by the operator, or emailed by the
// site) and the page that uses it to choose a new password. Its own file because the reset
// is one flow with its own rules, and the link is minted in one place for both ways in.

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>The password reset handlers of AccountEndpoints; the type and what it is for are described in AccountEndpoints.cs.</summary>
public static partial class AccountEndpoints
{
    /// <summary>
    /// Mints a reset link for an address on this site's store, behind the operator's key, and answers it for the
    /// operator to hand over.
    /// </summary>
    private static async Task<IResult> OperatorResetLink(ResetLinkRequest request, string? key, HttpContext http, IServiceProvider services, CurrentBackend current, TokenIssuer issuer, IResetLinks links, AdminKey adminKey, IConfiguration configuration, CancellationToken cancellation)
    {
        // This site as a visitor reaches it, for the link (Site:Url; unset, the request's host is used).
        string? siteUrl = configuration["Site:Url"];
        string? presented = key ?? http.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (!adminKey.Admits(presented))
        {
            return Results.NotFound();
        }

        if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
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

        string url = await ResetLinkFor(http, issuer, links, siteUrl, user, current.Backend.Key, cancellation);
        return Results.Json(new
        {
            email = user.Email,
            store = current.Backend.Key,
            url,
            expires_at = DateTimeOffset.UtcNow + TokenIssuer.ResetLifetime,
        });
    }

    /// <summary>
    /// Emails a reset link to an address, answering the same sentence whether or not the address has an account
    /// here.
    /// </summary>
    private static async Task<Results<Ok<ForgotReply>, ProblemHttpResult>> Forgot(ResetLinkRequest request, HttpContext http, IServiceProvider services, CurrentBackend current, TokenIssuer issuer, IEmailSender mail, ForgotLimit limit, IResetLinks links, IConfiguration configuration, CancellationToken cancellation)
    {
        // This site as a visitor reaches it, for the link (Site:Url; unset, the request's host is used).
        string? siteUrl = configuration["Site:Url"];
        if (!mail.Configured)
        {
            return TypedResults.Problem(
                detail: "This site cannot send email: " + mail.Reason + ".",
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "No email from here");
        }

        if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
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
            string url = await ResetLinkFor(http, issuer, links, siteUrl, user, current.Backend.Key, cancellation);
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
    /// minted for.
    /// </summary>
    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Reset(ResetRequest request, HttpContext http, IServiceProvider services, CurrentBackend current, TokenIssuer issuer, IResetLinks links, CancellationToken cancellation)
    {
        if (!current.Backend.Ready || services.GetService<UserManager<YardUser>>() is not { } users)
        {
            return Accounts.Unavailable();
        }

        // One sentence for every way the link can be wrong: expired, used,
        // forged, a GUID that names nothing, or a session token dressed as one.
        // The link carries a GUID; the token it stands for is read from the
        // store and checked exactly as before, and the GUID is forgotten right
        // before the password changes, so two takers get one change.
        var refused = TypedResults.Problem(
            detail: "That reset link is not valid any more. Ask for a new one.",
            statusCode: 400, title: "The link did not work");
        string? kept = string.IsNullOrWhiteSpace(request.Token) ? null : await links.ReadAsync(request.Token, cancellation);
        var claims = await issuer.ReadResetAsync(kept);
        if (claims is null)
        {
            return refused;
        }

        if (!string.Equals(claims.Value.Store, current.Backend.Key, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                detail: "That reset link belongs to the other site. Open it there.",
                statusCode: 400, title: "The link did not work");
        }

        var user = await users.FindByIdAsync(claims.Value.UserId);
        if (user is null || TokenIssuer.Fingerprint(user.PasswordHash) != claims.Value.Fingerprint)
        {
            return refused;
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.Problem(detail: "Choose a new password of eight characters or more.", statusCode: 400, title: "The password was not changed");
        }

        // Validated before anything is removed, so a refused password leaves the
        // old one in place rather than an account with none.
        foreach (var validator in users.PasswordValidators)
        {
            var verdict = await validator.ValidateAsync(users, user, request.Password);
            if (!verdict.Succeeded)
            {
                return TypedResults.Problem(detail: Accounts.Explain(verdict), statusCode: 400, title: "The password was not changed");
            }
        }

        // The link is spent here, before the change: a second taker finds
        // nothing to forget and is refused above the password ever moves.
        if (!await links.ForgetAsync(request.Token!, cancellation))
        {
            return refused;
        }

        // Removed then added rather than reset through a token provider, because
        // the provider needs a key ring this container does not keep across a
        // roll; both calls update the security stamp, and the fingerprint above
        // was what made the link one-use before the store did.
        var removed = await users.RemovePasswordAsync(user);
        if (!removed.Succeeded)
        {
            return TypedResults.Problem(detail: Accounts.Explain(removed), statusCode: 400, title: "The password was not changed");
        }

        var added = await users.AddPasswordAsync(user, request.Password);
        if (!added.Succeeded)
        {
            return TypedResults.Problem(detail: Accounts.Explain(added), statusCode: 400, title: "The password was not changed");
        }

        await users.ResetAccessFailedCountAsync(user);
        http.Response.Cookies.Append(
            TokenIssuer.CookieName,
            issuer.Issue(user.Id, user.Email!, current.Backend.Key),
            TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Describe(user));
    }

    /// <summary>
    /// Mints a reset link for the site the request came through, as the visitor reaches
    /// it: the site's own configured address first (Site:Url, the domain behind the edge;
    /// the request's host there is the origin, and an origin is not an address anybody
    /// should be sent), the forwarded host or the request's host otherwise. What the link
    /// carries is a GUID and nothing else: the signed token that names the account, the
    /// site and the password's fingerprint is kept under that GUID for the hour and
    /// forgotten on use (ADR: Accounts and per-user bids, the addendum on what the link
    /// looks like). Minted the same way whoever hands it over.
    /// </summary>
    private static async Task<string> ResetLinkFor(HttpContext http, TokenIssuer issuer, IResetLinks links, string? siteUrl, YardUser user, string store, CancellationToken cancellation)
    {
        string token = issuer.IssueReset(user.Id, store, user.PasswordHash);
        string id = await links.KeepAsync(token, TokenIssuer.ResetLifetime, cancellation);
        string origin;
        if (!string.IsNullOrWhiteSpace(siteUrl))
        {
            origin = siteUrl.TrimEnd('/');
        }
        else
        {
            string scheme = http.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Request.Scheme;
            string host = http.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? http.Request.Host.Value ?? "";
            origin = $"{scheme}://{host}";
        }

        return $"{origin}/?view=account&reset={id}";
    }
}
