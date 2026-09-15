using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>What the register and login forms send.</summary>
public sealed record Credentials(
    [property: Description("The address the account is under; one account per address on each store.")] string? Email,
    [property: Description("Eight characters or more; nothing else is required of it.")] string? Password);

/// <summary>What the operator sends to mint a reset link, and what a visitor sends to use one (ADR: Accounts and per-user bids, addendum).</summary>
public sealed record ResetLinkRequest(string? Email);

public sealed record ResetRequest(
    [property: Description("The GUID from the reset link; it works once, for an hour.")] string? Token,
    [property: Description("The new password, eight characters or more.")] string? Password);

/// <summary>
/// Who the browser is signed in as. Deliberately not the token: the page never
/// needs to read it, and a shape that carried it would invite somebody to put
/// it somewhere a script could reach (ADR: Accounts and per-user bids).
/// </summary>
public sealed record AccountView(
    [property: Description("False for a visitor with no session on this store; the other two fields are null then.")] bool SignedIn,
    string? Email,
    [property: Description("When the account was created, in milliseconds since the epoch, UTC.")] long? MemberSinceMs);

public static class Accounts
{
    public static readonly AccountView Anonymous = new(false, null, null);

    // #region accounts
    /// <summary>
    /// The store did not come up, so there are no accounts. 503 rather than
    /// 500: nothing is broken, a dependency is missing, and the difference
    /// matters to whoever reads it (ADR: The relational store).
    /// </summary>
    public static ProblemHttpResult Unavailable() => TypedResults.Problem(
        detail: "Accounts need the database, and it did not come up on this container. "
            + "The inventory is served from files and browsing still works.",
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Accounts are unavailable");

    /// <summary>
    /// One sentence for every way a registration can fail, rather than
    /// Identity's list of codes. The list is useful to a developer and is in
    /// the log; a person filling in a form needs to know what to change.
    /// </summary>
    public static string Explain(IdentityResult result) =>
        result.Errors.Any(e => e.Code.Contains("Password", StringComparison.Ordinal))
            ? "That password is too short. Eight characters or more."
            : result.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.Ordinal))
                ? "There is already an account with that email address."
                : "That email address does not look right.";

    public static AccountView Describe(YardUser user) =>
        new(true, user.Email, user.CreatedAtMs);
    // #endregion accounts
}
