using TheYard.Application.Features.Bids;

namespace TheYard.Api.Features.Bids;

/// <summary>
/// Bidding (ADR: Accounts and per-user bids): a bid, a Buy Now, the caller's badges and history,
/// and the room's round. Each route asks one handler in the Application ring's Features/Bids,
/// handed the request's auction, which the shared wiring composes once per request, and the
/// server's clock (ADR: The onion cut into vertical slices, rule 7).
/// </summary>
public sealed class Module : IModule
{
    /// <summary>
    /// Registers the slice's handlers, one per request, over the request store's parts: the queries
    /// get the vehicles as they stand, the commands get the writing side of what they move.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        static CurrentBackend Current(IServiceProvider provider) => provider.GetRequiredService<CurrentBackend>();
        services.AddScoped(provider => new PlaceBidHandler(Current(provider).Standings, Current(provider).Backend.Bids, Clocks.Now));
        services.AddScoped(provider => new ReadMyBidsHandler(Current(provider).Standings));
        services.AddScoped(provider => new ReadBidHistoryHandler(Current(provider).Standings));
        services.AddScoped(provider => new RoomRoundHandler(Current(provider).Backend.Bids, Current(provider).Backend.Inventory, Current(provider).Backend.Market, Current(provider).Standings, Clocks.Now));
        services.AddScoped(provider => new ResetBidsHandler(Current(provider).Backend.Bids, Current(provider).Backend.Market));
    }

    /// <summary>Maps /api/vehicles/{id}/bids, /api/vehicles/{id}/buy-now, /api/bids and /api/market/tick.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapBidEndpoints();
}
