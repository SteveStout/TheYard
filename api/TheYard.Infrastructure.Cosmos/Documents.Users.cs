// The two document shapes of the users container: the account, and the claim on an email
// address that keeps two accounts from sharing one. One file per container, so what a container
// holds is one short file to open; ADR: Accounts on a document store quotes the region below.
using System.Text.Json.Serialization;

namespace TheYard.Infrastructure.Cosmos;

// #region documents
/// <summary>
/// One account, whole, in one document. Identity's relational shape is a user
/// row plus claims, logins, tokens and roles in separate tables; this application
/// uses none of those tables, so the document holds exactly the columns the user
/// row holds that it reads (ADR: Accounts on a document store).
/// </summary>
public sealed class UserDocument
{
    /// <summary>The account id, which is also the partition key (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The name the account signs in with, which is its email address (<c>user_name</c>).</summary>
    public string? UserName { get; set; }

    /// <summary>The user name as Identity normalizes it for lookups (<c>normalized_user_name</c>).</summary>
    public string? NormalizedUserName { get; set; }

    /// <summary>The account's email address (<c>email</c>).</summary>
    public string? Email { get; set; }

    /// <summary>The email address as Identity normalizes it for lookups (<c>normalized_email</c>).</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>Whether the address has been confirmed (<c>email_confirmed</c>).</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>The password, as Identity's hasher wrote it; never the password itself (<c>password_hash</c>).</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Identity's stamp, changed whenever the credentials change so older sign-ins stop working (<c>security_stamp</c>).</summary>
    public string? SecurityStamp { get; set; }

    /// <summary>When a lockout ends, or null when the account is not locked out (<c>lockout_end</c>).</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Whether failed guesses can lock this account out (<c>lockout_enabled</c>).</summary>
    public bool LockoutEnabled { get; set; }

    /// <summary>How many wrong passwords in a row since the last good one (<c>access_failed_count</c>).</summary>
    public int AccessFailedCount { get; set; }

    /// <summary>When the account was created, in milliseconds since the epoch, UTC (<c>created_at_ms</c>).</summary>
    public long CreatedAtMs { get; set; }

    /// <summary>The document's version, kept by the store and used as the concurrency stamp (<c>_etag</c>).</summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }
}

/// <summary>
/// The claim on an email address. A store with no unique index across
/// partitions cannot promise two accounts will not share an address; a document
/// whose id is the address can, because a second create of the same id is
/// refused with 409. Registering writes this first and the account second
/// (ADR: Accounts on a document store).
/// </summary>
public sealed class EmailClaimDocument
{
    /// <summary>What every claim's id starts with, so a claim can never share an id with an account.</summary>
    public const string Prefix = "email:";

    /// <summary>The prefix and the normalized address, which is also the partition key (<c>id</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The account that holds the address (<c>user_id</c>).</summary>
    public string UserId { get; set; } = "";

    /// <summary>
    /// When the store last wrote it, in seconds since the epoch, set by the
    /// service and never by this code: null on the way in, so nothing is
    /// written, and the service's own value on the way back. The account
    /// store reads it to tell an orphaned claim from one whose account is a
    /// moment away (ADR: Accounts on a document store, addendum).
    /// </summary>
    [JsonPropertyName("_ts")]
    public long? Timestamp { get; set; }

    /// <summary>The claim id for a normalized address.</summary>
    public static string IdFor(string normalizedEmail) => Prefix + normalizedEmail;
}
// #endregion documents
