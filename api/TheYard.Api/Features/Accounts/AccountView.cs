using System.ComponentModel;

namespace TheYard.Api.Features.Accounts;

/// <summary>
/// Who the browser is signed in as. Deliberately not the token: the page never
/// needs to read it, and a shape that carried it would invite somebody to put
/// it somewhere a script could reach (ADR: Accounts and per-user bids).
/// </summary>
/// <param name="SignedIn">False for a visitor with no session on this store; the other two fields are null then.</param>
/// <param name="Email">The signed-in account's email address, or null for a visitor.</param>
/// <param name="MemberSinceMs">When the account was created, in milliseconds since the epoch, UTC.</param>
public sealed record AccountView(
    [property: Description("False for a visitor with no session on this store; the other two fields are null then.")] bool SignedIn,
    string? Email,
    [property: Description("When the account was created, in milliseconds since the epoch, UTC.")] long? MemberSinceMs);
