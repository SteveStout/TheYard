using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// Every Claude lane's usage (ADR: Every Claude lane reports its usage): the log the documents are
/// kept in, which the Admin tab's usage route writes and the usage card reads, and the one client
/// this site's own code asks Claude through, which writes a document from every answer.
/// </summary>
public static class UsageRegistration
{
    /// <summary>Registers the usage log and the Claude client.</summary>
    /// <param name="builder">The host being built.</param>
    public static void AddTheYardUsage(this WebApplicationBuilder builder)
    {
        // #region usage-wiring
        // Kept in the plan container where there is a document store, in this process's memory
        // where there is none. The client's key is configuration's (Claude:ApiKey), never the
        // repository's; without one the client refuses to ask, and nothing asks yet.
        builder.Services.AddSingleton<IUsageLog>(provider => provider.GetRequiredService<DocumentStore>().Store is { } store
            ? (IUsageLog)new CosmosUsageLog(store)
            : new MemoryUsageLog("kept in this process's memory, so the usage documents end with it; the document store is not configured here"));
        string? key = builder.Configuration["Claude:ApiKey"];
        builder.Services.AddSingleton<IClaudeClient>(provider => new AnthropicClaudeClient(key, provider.GetRequiredService<IUsageLog>(), TimeProvider.System));
        // #endregion usage-wiring
    }
}
