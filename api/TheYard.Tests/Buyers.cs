using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// A browser with an account in it (ADR: Accounts and per-user bids). Bidding
/// needs one now, and the tests that were about bidding rules should not have
/// to become tests about registration to keep working.
/// </summary>
internal static class Buyers
{
    // #region signed-in
    /// <summary>
    /// A client with a session cookie. The email is unique per call because a
    /// factory is one database and registering the same address twice is a
    /// duplicate, which would make the order tests run in matter.
    /// </summary>
    internal static async Task<HttpClient> SignedIn(WebApplicationFactory<Program> api)
    {
        var client = api.CreateClient();
        var registered = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"buyer-{Guid.NewGuid():N}@example.com", password = "correct horse" });
        registered.EnsureSuccessStatusCode();
        return client;
    }
    // #endregion signed-in
}
