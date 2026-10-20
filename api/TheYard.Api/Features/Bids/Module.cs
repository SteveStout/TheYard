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
    /// <summary>Registers the slice's handlers, one per request, each over the request's auction.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped(provider => new PlaceBidHandler(provider.GetRequiredService<CurrentBackend>().Auction, Clocks.Now));
        services.AddScoped(provider => new MyBidsHandler(provider.GetRequiredService<CurrentBackend>().Auction));
        services.AddScoped(provider => new BidHistoryHandler(provider.GetRequiredService<CurrentBackend>().Auction));
        services.AddScoped(provider => new RoomRoundHandler(provider.GetRequiredService<CurrentBackend>().Auction, Clocks.Now));
        services.AddScoped(provider => new ResetBidsHandler(provider.GetRequiredService<CurrentBackend>().Auction));
    }

    /// <summary>Maps /api/vehicles/{id}/bids, /api/vehicles/{id}/buy-now, /api/bids and /api/market/tick.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapBidEndpoints();
}
