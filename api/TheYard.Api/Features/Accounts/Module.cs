using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using TheYard.Application;
using TheYard.Application.Features.Accounts;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;
using TheYard.Infrastructure.Features.Accounts;

namespace TheYard.Api.Features.Accounts;

/// <summary>
/// Accounts (ADR: Accounts and per-user bids): the registration allowance, Identity over whichever
/// store the request chose, the JWT the service reads back from its cookie and the refusal of a
/// session that has ended, the one email the site sends, and the account routes. The session issuer
/// itself is shared, because the pipeline renews sessions and the activity card keys its visitor
/// tokens with the same key (Shared/Wiring/AuthRegistration.cs).
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the registration allowance, Identity over the request's store, the cookie-borne JWT and the reset email.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration: the allowance, the mail settings and the identity.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region auth
        // The hour's allowance of new accounts, for the whole site (ADR: The one write
        // a stranger can make). Configurable because the tests need to reach it, and
        // because a number that cannot be changed without a deploy is a number nobody
        // tunes.
        services.AddSingleton(new RegistrationLimit(
            configuration.GetValue("Accounts:RegistrationsPerHour", RegistrationLimit.DefaultPerHour),
            () => DateTimeOffset.UtcNow));

        // Identity is registered whether or not a store came up, because the store
        // is now a per-request choice: the same UserManager serves the relational
        // accounts on one request and the document accounts on the next, and a
        // request on a backend that has no accounts is refused by the endpoint before
        // it asks for one (ADR: One container, both stores).
        services
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
        // (ADR: Accounts on a document store), each registered under its store's key by the
        // store's own registration, so the backend itself names no adapter (ADR: The onion cut
        // into vertical slices, rule 4). One of the four scoped registrations in the
        // application, and the reason the per-request context exists.
        services.AddScoped<IUserStore<YardUser>>(services =>
            services.GetKeyedService<IUserStore<YardUser>>(services.GetRequiredService<CurrentBackend>().Key)
                ?? throw new InvalidOperationException("this request's store keeps no accounts; RequestAccounts answers null before this is reached"));

        // The handlers take RequestAccounts rather than UserManager, because UserManager is built
        // over the request's store, and a store that did not come up has no accounts to build it
        // over. RequestAccounts asks first and answers null, so no handler meets the throw above.
        services.AddScoped(services => new RequestAccounts(() =>
            services.GetRequiredService<CurrentBackend>().Backend.Ready
                ? services.GetRequiredService<UserManager<YardUser>>()
                : null));
        // #endregion user-store-per-request
        // #region account-handlers
        // One handler per route (ADR: The onion cut into vertical slices, rule 7), each handed the
        // request's accounts through the Identity adapter, or none where the request's store keeps
        // no accounts, which every handler answers as unavailable.
        services.AddScoped(provider => provider.GetRequiredService<RequestAccounts>().Users is { } users
            ? new AccountsOnThisStore(new IdentityAccountStore(users))
            : new AccountsOnThisStore(null));
        services.AddSingleton<IResetTokens>(provider => new TokenResets(provider.GetRequiredService<TokenIssuer>()));
        services.AddSingleton(provider => new ResetLinkMaker(provider.GetRequiredService<IResetTokens>(), provider.GetRequiredService<IResetLinks>()));
        services.AddScoped(provider => new RegisterHandler(Store(provider), provider.GetRequiredService<RegistrationLimit>(), () => Clocks.UtcNow().ToUnixTimeMilliseconds()));
        services.AddScoped(provider => new SignInHandler(Store(provider)));
        services.AddScoped(provider => new SignOutHandler(Store(provider)));
        services.AddScoped(provider => new WhoAmIHandler(Store(provider)));
        services.AddScoped(provider => new OperatorResetLinkHandler(Store(provider), provider.GetRequiredService<ResetLinkMaker>()));
        services.AddScoped(provider => new ForgotHandler(Store(provider), provider.GetRequiredService<IEmailSender>(), provider.GetRequiredService<ForgotLimit>(), provider.GetRequiredService<ResetLinkMaker>()));
        services.AddScoped(provider => new ResetPasswordHandler(Store(provider), provider.GetRequiredService<IResetTokens>(), provider.GetRequiredService<IResetLinks>()));
        // #endregion account-handlers
        // What the JWT reader does with a token, per request and with its dependencies handed in
        // (RefuseSession.cs): read it from the cookie, then refuse a session that has ended.
        services.AddScoped<RefuseSession>();
        services.AddScoped<SessionEvents>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
                // Resolved from the request's own services for each request, so the events are
                // handed the request's store and accounts by constructor.
                options.EventsType = typeof(SessionEvents));
        // The token's signature and lifetime are checked against the session issuer's rules.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TokenIssuer>((options, tokens) => options.TokenValidationParameters = tokens.Validation);
        services.AddAuthorization();
        // #endregion auth

        // #region email-wiring
        // The one email this site sends, the reset link, through Azure Communication
        // Services as the containers' own identity; two plain settings and no key.
        // Unset, the "Forgot password" endpoint says so and the operator's link from
        // the Admin tab is the way (ADR: Accounts and per-user bids, addendum).
        services.AddSingleton<IEmailSender>(services => AcsEmailSender.FromConfiguration(
            configuration["Email:Endpoint"],
            configuration["Email:From"],
            // The same identity the stores use, chosen by the same setting.
            () => CosmosStore.CredentialFor(
                configuration["Cosmos:Credential"] ?? "azure-cli",
                AzureIds.ClientId(configuration)),
            services.GetRequiredService<ILogger<AcsEmailSender>>()));
        services.AddSingleton(new ForgotLimit(ForgotLimit.DefaultSpacing, () => DateTimeOffset.UtcNow));
        // #endregion email-wiring
    }

    /// <summary>The request's accounts, or none.</summary>
    private static IAccountStore? Store(IServiceProvider provider) => provider.GetRequiredService<AccountsOnThisStore>().Store;

    /// <summary>The request's accounts, or none where its store keeps no accounts; one per request.</summary>
    /// <param name="Store">The accounts, or null.</param>
    private sealed record AccountsOnThisStore(IAccountStore? Store);

    /// <summary>Maps /api/auth/* and the operator's reset link.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapAccountEndpoints();
}
