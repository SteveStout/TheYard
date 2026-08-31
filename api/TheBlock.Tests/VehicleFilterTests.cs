using TheBlock.Domain;

namespace TheBlock.Tests;

public class VehicleFilterTests
{
    private static readonly AuctionClock Now = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(new VehicleFilter().Matches(TestData.Vehicle(), Now));
    }

    [Fact]
    public void Query_tokens_match_year_make_model_and_trim_case_insensitively()
    {
        var vehicle = TestData.Vehicle(); // 2023 Ford Bronco Big Bend

        Assert.True(new VehicleFilter { Query = "ford BRO" }.Matches(vehicle, Now));
        Assert.True(new VehicleFilter { Query = "  2023   big bend " }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Query = "honda" }.Matches(vehicle, Now));
    }

    [Fact]
    public void Query_tokens_match_every_filterable_field()
    {
        // SUV, clean title, Toronto, Ontario in the fixture.
        var vehicle = TestData.Vehicle();

        Assert.True(new VehicleFilter { Query = "suv" }.Matches(vehicle, Now));
        Assert.True(new VehicleFilter { Query = "clean" }.Matches(vehicle, Now));
        Assert.True(new VehicleFilter { Query = "ontario toronto" }.Matches(vehicle, Now));
        Assert.True(new VehicleFilter { Query = "ford ontario clean suv" }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Query = "salvage" }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Query = "quebec" }.Matches(vehicle, Now));
    }

    [Fact]
    public void Query_matches_the_derived_auction_status()
    {
        var vehicle = TestData.Vehicle();
        string actual = AuctionSchedule.StatusFor(vehicle.Id, Now).ToString().ToLowerInvariant();
        string other = actual == "live" ? "ended" : "live";

        Assert.True(new VehicleFilter { Query = actual }.Matches(vehicle, Now));
        Assert.True(new VehicleFilter { Query = $"ford {actual}" }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Query = other }.Matches(vehicle, Now));
    }

    [Fact]
    public void Exact_filters_compare_case_insensitively()
    {
        var vehicle = TestData.Vehicle(make: "Ford", bodyStyle: "SUV");

        Assert.True(new VehicleFilter { Make = "ford", BodyStyle = "suv" }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Make = "Honda" }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Province = "Quebec" }.Matches(vehicle, Now));
    }

    [Fact]
    public void Minimum_condition_is_inclusive()
    {
        var vehicle = TestData.Vehicle(); // grade 3.8

        Assert.True(new VehicleFilter { MinCondition = 3.8 }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { MinCondition = 3.9 }.Matches(vehicle, Now));
    }

    [Fact]
    public void Price_bounds_use_the_high_bid_when_present()
    {
        var vehicle = TestData.Vehicle(currentBid: 22800);

        Assert.True(new VehicleFilter { PriceMin = 22800, PriceMax = 22800 }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { PriceMax = 22799 }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { PriceMin = 22801 }.Matches(vehicle, Now));
    }

    [Fact]
    public void Price_bounds_fall_back_to_the_opening_ask_before_any_bids()
    {
        var vehicle = TestData.Vehicle(currentBid: null); // starting bid 14,500

        Assert.True(new VehicleFilter { PriceMin = 14500, PriceMax = 14500 }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { PriceMin = 14501 }.Matches(vehicle, Now));
    }

    [Fact]
    public void Status_filter_matches_the_derived_auction_status()
    {
        var vehicle = TestData.Vehicle(id: "some-id");
        var actual = AuctionSchedule.StatusFor("some-id", Now);
        var other = actual == AuctionStatus.Live ? AuctionStatus.Ended : AuctionStatus.Live;

        Assert.True(new VehicleFilter { Status = actual }.Matches(vehicle, Now));
        Assert.False(new VehicleFilter { Status = other }.Matches(vehicle, Now));
    }
}
