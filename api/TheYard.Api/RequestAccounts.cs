// The accounts of the store a request chose, as the account handlers need them: Identity's
// UserManager over that store, or nothing when the store keeps no accounts. Built in
// Composition/AuthRegistration.cs, so a handler takes this as a parameter and never reaches
// into the service container itself.
using Microsoft.AspNetCore.Identity;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// The request's accounts. Opened on first use and not before, so a request that never touches
/// an account never builds the store behind one.
/// </summary>
/// <param name="open">Opens UserManager over the request's store, or answers null when that store keeps no accounts.</param>
public sealed class RequestAccounts(Func<UserManager<YardUser>?> open)
{
    /// <summary>Opened once per request, on first use.</summary>
    private readonly Lazy<UserManager<YardUser>?> _users = new(open);

    /// <summary>UserManager over the request's store, or null when that store did not come up and keeps no accounts.</summary>
    public UserManager<YardUser>? Users => _users.Value;
}
