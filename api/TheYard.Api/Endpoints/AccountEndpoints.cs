using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using TheYard.Application;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// Accounts: register, sign in and out, who am I, and the password reset in its
/// two halves (the operator's link and the emailed one). The token never reaches
/// the page; it travels in an httpOnly cookie.
/// </summary>
public static class AccountEndpoints
{
    /// <summary>Maps the account routes under /api/auth, and the operator's reset link.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        #region auth-endpoints
        // Register, sign in, sign out, and who am I. The token never reaches the page:
        // it is set as an httpOnly cookie on the way out and read from the cookie on the
        // way back in, so a script on the page cannot read it and cannot be tricked into
        // sending it somewhere else (ADR: Accounts and per-user bids).
        app.MapPost("/api/auth/register", Register)
            .WithName("Register")
            .WithTags("Accounts")
            .WithSummary("Create an account on this store and sign in")
            .WithDescription("Answers the account and sets the session cookie. A few dozen registrations an hour are allowed "
                + "across all strangers, and a refused password gives the slot back.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/auth/login", Login)
            .WithName("Login")
            .WithTags("Accounts")
            .WithSummary("Sign in and receive the session cookie")
            .WithDescription("One sentence for every way this can fail, including a locked account, so the reply never says "
                + "which addresses are registered here. Five wrong passwords lock an account for five minutes.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // #region password-reset
        // A password reset in two halves (ADR: Accounts and per-user bids, addendum).

        // The operator mints a link from the Admin tab, behind the key, for an
        // address on this site's store: an hour of life, one use, because the token
        // carries a fingerprint of the password hash it was minted against and the
        // hash changes when the password does. The visitor opens the link, chooses a
        // new password, and is signed in. The same second half will serve an emailed
        // link when there is a sender to send it; only who hands the link over
        // changes.
        app.MapPost("/api/admin/reset-links", OperatorResetLink);

        // "Forgot password": the same link, sent by the site instead of handed over
        // by the operator. Public, so it answers one sentence whether or not the
        // address has an account here, and one email per address per five minutes.
        // Without a sender configured it says so and points at the operator.
        app.MapPost("/api/auth/forgot", Forgot)
            .WithName("ForgotPassword")
            .WithTags("Accounts")
            .WithSummary("Email a reset link to an address")
            .WithDescription("One sentence back whether or not the address has an account here, and one email per address "
                + "per five minutes. A container with no sender configured says so.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/auth/reset", Reset)
            .WithName("ResetPassword")
            .WithTags("Accounts")
            .WithSummary("Use a reset link to choose a new password and sign in")
            .WithDescription("The link works once, for an hour, on the site it was minted for. A refused password leaves the "
                + "old one in place.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        // #endregion password-reset

        app.MapPost("/api/auth/logout", Logout)
            .WithName("Logout")
            .WithTags("Accounts")
            .WithSummary("Sign out")
            .WithDescription("Deletes the session cookie and answers the anonymous account. Never fails.");

        app.MapGet("/api/auth/me", WhoAmI)
            .WithName("WhoAmI")
            .WithTags("Accounts")
            .WithSummary("Who the session belongs to on this store")
            .WithDescription("Signed out is an answer, not a 401: a session opened on the other store reads as signed out here.");
        #endregion auth-endpoints

        return app;
    }

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

    private static Ok<AccountView> Logout(TokenIssuer issuer, HttpContext http)
    {
        // Deleted with the same attributes it was set with, or the browser keeps a
        // second cookie of the same name on a different path and stays signed in.
        http.Response.Cookies.Delete(TokenIssuer.CookieName, TokenIssuer.CookieFor(http, issuer.Lifetime));
        return TypedResults.Ok(Accounts.Anonymous);
    }

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

    // The link, for the site the request came through, as the visitor reaches
    // it: the site's own configured address first (Site:Url, the domain behind
    // the edge; the request's host there is the origin, and an origin is not an
    // address anybody should be sent), the forwarded host or the request's host
    // otherwise. What the link carries is a GUID and nothing else: the signed
    // token that names the account, the site and the password's fingerprint is
    // kept under that GUID for the hour and forgotten on use (addendum of 14
    // September). Minted the same way whoever hands it over.
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
