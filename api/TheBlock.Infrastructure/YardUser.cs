using Microsoft.AspNetCore.Identity;

namespace TheBlock.Infrastructure;

/// <summary>
/// A buyer with an account (ADR: Accounts and per-user bids).
///
/// `IdentityUser` already carries the identity, the email, the normalised
/// lookup keys and the password hash, which is most of what a user is and all
/// of the parts that are easy to get wrong. The one field added here is the
/// only thing this application knows about a person that Identity does not.
/// </summary>
public sealed class YardUser : IdentityUser
{
    /// <summary>When the account was created, in the same milliseconds every other timestamp on this wire uses.</summary>
    public long CreatedAtMs { get; set; }
}
