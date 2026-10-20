using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// Accounts and sessions (ADR: Accounts and per-user bids): the signing key and the
/// token issuer first, because the activity feature keys its visitor tokens with the
/// same key, then Identity over whichever store the request chose and the JWT the
/// service reads back from its cookie.
/// </summary>
public static class AuthRegistration
{
    // #region auth
    /// <summary>Reads or invents the signing key, registers the session issuer and the visitor tokens keyed with it, and answers the issuer.</summary>
    /// <param name="builder">The host being built.</param>
    public static TokenIssuer AddTheYardSessions(this WebApplicationBuilder builder)
    {
        // Accounts (ADR: Accounts and per-user bids). Identity owns the password
        // hashing, the normalised lookups and the account tables, which is the part
        // worth not writing twice; the session is a JWT this service signs and reads
        // itself, carried in a cookie the page cannot touch.
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
        return tokens;
    }

    /// <summary>Registers the registration allowance, Identity over the request's store, and the cookie-borne JWT.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="tokens">The session issuer, whose validation the JWT reader uses.</param>
    public static void AddTheYardAccounts(this WebApplicationBuilder builder, TokenIssuer tokens)
    {
        // The hour's allowance of new accounts, for the whole site (ADR: The one write
        // a stranger can make). Configurable because the tests need to reach it, and
        // because a number that cannot be changed without a deploy is a number nobody
        // tunes.
        builder.Services.AddSingleton(new RegistrationLimit(
            builder.Configuration.GetValue("Accounts:RegistrationsPerHour", RegistrationLimit.DefaultPerHour),
            () => DateTimeOffset.UtcNow));

        // Identity is registered whether or not a store came up, because the store
        // is now a per-request choice: the same UserManager serves the relational
        // accounts on one request and the document accounts on the next, and a
        // request on a backend that has no accounts is refused by the endpoint before
        // it asks for one (ADR: One container, both stores).
        builder.Services
            .AddIdentityCore<YardUser>(options =>
            {
                // Long over ornate. A length requirement is the only one of these
                // that measurably helps, and the rest mostly teach people to write
                // the password down (NIST 800-63B says so at more length).
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;
                options.User.RequireUniqueEmail = true;
                // #region lockout
                // Five wrong passwords buys five minutes off.
                //
                // Without this, POST /api/auth/login
                // is an unmetered password oracle against real accounts: the endpoint
                // is public, there is no throttle in front of it, and every attempt
                // costs an attacker one request. Five and five is the usual shape and
                // the reason it works is arithmetic rather than strength: it turns
                // thousands of guesses a minute into twelve an hour, per account,
                // which is the difference between a wordlist finishing and not.
                //
                // The refusal after a lockout says the same sentence as a wrong
                // password, deliberately. A distinct "this account is locked" is a
                // reply that confirms the address is registered here, and the login
                // endpoint already goes out of its way not to be that
                // (ADR: A password guess should cost something).
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;
                // #endregion lockout
            });
        // #region user-store-per-request
        // The store behind UserManager, chosen by the request: Identity's own tables
        // on the relational backend, one document per account on the document one
        // (ADR: Accounts on a document store). One of the four scoped registrations
        // in the application, and the reason the per-request context exists.
        builder.Services.AddScoped<IUserStore<YardUser>>(services =>
            services.GetRequiredService<CurrentBackend>().Backend.UserStore(services)
                ?? throw new InvalidOperationException("this request's store keeps no accounts; RequestAccounts answers null before this is reached"));

        // The handlers take RequestAccounts rather than UserManager, because UserManager is built
        // over the request's store, and a store that did not come up has no accounts to build it
        // over. RequestAccounts asks first and answers null, so no handler meets the throw above.
        builder.Services.AddScoped(services => new RequestAccounts(() =>
            services.GetRequiredService<CurrentBackend>().Backend.Ready
                ? services.GetRequiredService<UserManager<YardUser>>()
                : null));
        // #endregion user-store-per-request
        builder.Services.AddSingleton<PasswordReset>();
        // What the JWT reader does with a token, per request and with its dependencies handed in
        // (RefuseSession.cs): read it from the cookie, then refuse a session that has ended.
        builder.Services.AddScoped<RefuseSession>();
        builder.Services.AddScoped<SessionEvents>();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = tokens.Validation;
                // Resolved from the request's own services for each request, so the events are
                // handed the request's store and accounts by constructor.
                options.EventsType = typeof(SessionEvents);
            });
        builder.Services.AddAuthorization();
    }
    // #endregion auth
}

/// <summary>Whether the session signing key came from configuration or was invented by this process at startup.</summary>
/// <param name="Configured">True when the deploy handed a usable key over.</param>
public sealed record SigningKeyOrigin(bool Configured);
