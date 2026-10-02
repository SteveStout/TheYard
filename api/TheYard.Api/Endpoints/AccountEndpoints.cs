// Accounts, and the place to start reading them. This file holds the class and the map of
// every account route. The handlers live beside it:
//   AccountEndpoints.SignIn.cs         register, sign in, sign out, and who am I
//   AccountEndpoints.PasswordReset.cs  the operator's reset link, the emailed one, and using either

namespace TheYard.Api;

/// <summary>
/// Accounts: register, sign in and out, who am I, and the password reset in its
/// two halves (the operator's link and the emailed one). The token never reaches
/// the page; it travels in an httpOnly cookie.
/// </summary>
public static partial class AccountEndpoints
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
}
