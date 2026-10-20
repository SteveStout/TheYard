using System.Security.Cryptography;

namespace TheYard.Api;

/// <summary>
/// Sessions (ADR: Accounts and per-user bids): the signing key and the token issuer, shared
/// because the pipeline renews a session and the activity feature keys its visitor tokens with the
/// same key. The accounts themselves are a slice (Features/Accounts/Module.cs).
/// </summary>
public static class AuthRegistration
{
    // #region sessions
    /// <summary>Reads or invents the signing key, and registers the session issuer and the visitor tokens keyed with it.</summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheYardSessions(this WebApplicationBuilder builder)
    {
        // The session is a JWT this service signs and reads itself, carried in a cookie the
        // page cannot touch (ADR: Accounts and per-user bids).
        //
        // The signing key is configuration. The deploy hands the web app one from a
        // repository secret (Auth__SigningKey in infra/appservice.bicep, filled at roll
        // time like the connection strings), so a session survives a roll. Without a usable one,
        // which is what a developer's machine, a test and a roll whose substitution
        // failed all have, the process invents a random key and says so: every session
        // ends with the process, and no key is ever committed (ADR: Three readers with
        // no memory of the project).
        string? configuredSigningKey = TokenIssuer.ConfiguredKey(builder.Configuration["Auth:SigningKey"]);
        string signingKey = configuredSigningKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        // A year, renewed on every day it is used (Tokens.cs, the renewal region):
        // a login lasts a year past the last visit, which is what "permanent" means
        // for a cookie that has to expire somewhere (ADR: Accounts and per-user
        // bids, addendum). Configuration, so a test can shorten it.
        var tokens = new TokenIssuer(signingKey, TimeSpan.FromDays(builder.Configuration.GetValue("Auth:SessionDays", 365)));
        builder.Services.AddSingleton(tokens);
        // Whether the key was configured or invented, which StoreAnnouncement says out loud at startup.
        builder.Services.AddSingleton(new SigningKeyOrigin(configuredSigningKey is not null));
        // The activity card's visitor token is keyed with the same key (ADR: Site activity, and
        // the line an address does not cross), so it is made here, where the key is.
        builder.Services.AddSingleton(new VisitorTokens(signingKey));
    }

    // #endregion sessions
}

/// <summary>Whether the session signing key came from configuration or was invented by this process at startup.</summary>
/// <param name="Configured">True when the deploy handed a usable key over.</param>
public sealed record SigningKeyOrigin(bool Configured);
