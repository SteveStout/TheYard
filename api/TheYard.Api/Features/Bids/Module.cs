namespace TheYard.Api.Features.Bids;

/// <summary>
/// Bidding (ADR: Accounts and per-user bids): a bid, a Buy Now, the caller's badges and history, and the room's round.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Nothing of its own to register yet: what its endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>Maps /api/vehicles/{id}/bids, /api/vehicles/{id}/buy-now, /api/bids and /api/market/tick.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapBidEndpoints();
    }
}
