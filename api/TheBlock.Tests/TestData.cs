using TheBlock.Domain;
using TheBlock.Data;

namespace TheBlock.Tests;

internal static class TestData
{
    /// <summary>An AuctionClock anchored at midnight of <paramref name="now"/>'s own day and offset.</summary>
    public static AuctionClock ClockAt(DateTimeOffset now) =>
        new(
            now.ToUnixTimeMilliseconds(),
            new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).ToUnixTimeMilliseconds());

    /// <summary>A valid vehicle with overridable fields tests care about.</summary>
    public static Vehicle Vehicle(
        string id = "test-id",
        string make = "Ford",
        string bodyStyle = "SUV",
        IReadOnlyList<string>? images = null,
        int? currentBid = 22800) => new()
    {
        Id = id,
        Vin = "TRD7L1KS0HNB5X3K3",
        Year = 2023,
        Make = make,
        Model = "Bronco",
        Trim = "Big Bend",
        BodyStyle = bodyStyle,
        ExteriorColor = "Burgundy",
        InteriorColor = "Beige",
        Engine = "2.7L EcoBoost V6",
        Transmission = "automatic",
        Drivetrain = "4WD",
        OdometerKm = 47731,
        FuelType = "gasoline",
        ConditionGrade = 3.8,
        ConditionReport = "Average condition.",
        DamageNotes = ["Scratch on liftgate"],
        TitleStatus = "clean",
        Province = "Ontario",
        City = "Toronto",
        AuctionStart = "2026-04-05T14:00:00",
        StartingBid = 14500,
        ReservePrice = 25000,
        BuyNowPrice = null,
        Images = images ?? ["https://placehold.co/800x600"],
        SellingDealership = "King City Auto",
        Lot = "A-0043",
        CurrentBid = currentBid,
        BidCount = 16,
    };

    public static IReadOnlyDictionary<string, IReadOnlyList<PhotoEntry>> Pools(
        params (string Style, PhotoEntry[] Photos)[] pools) =>
        pools.ToDictionary(p => p.Style, p => (IReadOnlyList<PhotoEntry>)p.Photos.ToList());

    public static PhotoEntry[] SuvPool =>
    [
        new("suv-01.jpg", "suv", "File:2021 Ford Bronco Big Bend.jpg"),
        new("suv-02.jpg", "suv", "File:21 Ford Bronco.jpg"),
        new("suv-03.jpg", "suv", "File:Jeep Wrangler Rubicon (JL).jpg"),
        new("suv-04.jpg", "suv", "File:Toyota RAV4 XA50.jpg"),
        new("suv-05.jpg", "suv", "File:Honda CR-V (6th generation).jpg"),
        new("suv-06.jpg", "suv", "File:Hyundai Tucson (NX4).jpg"),
    ];
}
