using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The one email this site sends, the reset link, through Azure Communication
/// Services as the containers' own identity (ADR: Accounts and per-user bids, addendum).
/// </summary>
public static class EmailRegistration
{
    /// <summary>Registers the sender and the one-per-five-minutes limit.</summary>
    public static void AddTheYardEmail(this WebApplicationBuilder builder)
    {
        // #region email-wiring
        // The one email this site sends, the reset link, through Azure Communication
        // Services as the containers' own identity; two plain settings and no key.
        // Unset, the "Forgot password" endpoint says so and the operator's link from
        // the Admin tab is the way (ADR: Accounts and per-user bids, addendum).
        builder.Services.AddSingleton<IEmailSender>(services => AcsEmailSender.FromConfiguration(
            builder.Configuration["Email:Endpoint"],
            builder.Configuration["Email:From"],
            // The same identity the stores use, chosen by the same setting.
            () => CosmosStore.CredentialFor(
                builder.Configuration["Cosmos:Credential"] ?? "azure-cli",
                builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5"),
            services.GetRequiredService<ILogger<AcsEmailSender>>()));
        builder.Services.AddSingleton(new ForgotLimit(ForgotLimit.DefaultSpacing, () => DateTimeOffset.UtcNow));
        // #endregion email-wiring
    }
}
