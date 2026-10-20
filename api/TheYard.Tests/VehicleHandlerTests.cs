using TheYard.Application.Features.Vehicles;
using TheYard.Domain;

namespace TheYard.Tests;

// #region vehicle-handler-tests
/// <summary>The Vehicles slice's handlers with fakes at the auction's ports and no host (ADR: The onion cut into vertical slices, rule 7).</summary>
public sealed class VehicleHandlerTests
{
    [Fact]
    public async Task A_page_the_facets_and_one_vehicle_come_from_the_auction_and_an_unknown_id_is_none()
    {
        var first = TestAuctions.Live("one");
        var second = TestAuctions.Live("two");
        var auction = TestAuctions.Over(first, second);

        var page = await new SearchVehiclesHandler(auction).HandleAsync(new VehicleSearch(new VehicleFilter(), TestAuctions.AtNoon(), VehicleSort.EndingSoonest, 10, 0), CancellationToken.None);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Vehicles.Count);

        var facets = await new FacetsHandler(auction).HandleAsync(CancellationToken.None);
        Assert.Contains("Ford", facets.Makes);

        var found = await new FindVehicleHandler(auction, TestAuctions.AtNoon).HandleAsync(first.Id, CancellationToken.None);
        Assert.Equal(first.Id, found.Vehicle!.Vehicle.Id);
        Assert.Equal(TestAuctions.AtNoon(), found.Clock);
        Assert.Null((await new FindVehicleHandler(auction, TestAuctions.AtNoon).HandleAsync("nobody-has-this-id", CancellationToken.None)).Vehicle);
    }
}
// #endregion vehicle-handler-tests
