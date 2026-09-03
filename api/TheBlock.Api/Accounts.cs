using Microsoft.AspNetCore.Identity;
using TheBlock.Infrastructure;

namespace TheBlock.Api;

/// <summary>What the register and login forms send.</summary>
public sealed record Credentials(string? Email, string? Password);

/// <summary>
/// Who the browser is signed in as. Deliberately not the token: the page never
/// needs to read it, and a shape that carried it would invite somebody to put
/// it somewhere a script could reach (ADR: Accounts and per-user bids).
/// </summary>
public sealed record AccountView(bool SignedIn, string? Email, long? MemberSinceMs);

public static class Accounts
{
    public static readonly AccountView Anonymous = new(false, null, null);

    // #region accounts
    /// <summary>
    /// The store did not come up, so there are no accounts. 503 rather than
    /// 500: nothing is broken, a dependency is missing, and the difference
    /// matters to whoever reads it (ADR: The relational store).
    /// </summary>
    public static IResult Unavailable() => Results.Problem(
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
