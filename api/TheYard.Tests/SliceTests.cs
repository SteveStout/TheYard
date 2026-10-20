using TheYard.Api;

namespace TheYard.Tests;

// #region slice-tests
/// <summary>
/// The second axis of the onion (ADR: The onion cut into vertical slices): the rules that hold a
/// feature to one folder per ring and its dependencies to what it is handed. The ring rules are in
/// OnionTests; these are the rules the record added beside them, one rung of its ladder at a time.
/// </summary>
public sealed class SliceTests
{
    /// <summary>The host's assembly, read by name so a type that no longer exists is a null and not a compile error.</summary>
    private static readonly System.Reflection.Assembly Host = typeof(BidEndpoints).Assembly;

    [Fact]
    public void Rule_2_the_composition_bag_is_retired_and_startup_is_one_hosted_service_per_concern()
    {
        // The record every registration took whole, and the one method that read eleven fields off
        // it, are gone: each registration is handed what it needs by name, and what ran at startup
        // is a hosted service per concern, each taking its dependencies by constructor.
        Assert.Null(Host.GetType("TheYard.Api.YardComposition"));
        Assert.Null(Host.GetType("TheYard.Api.Startup"));
        string[] concerns = ["StoreAnnouncement", "ScratchDatabaseCleanup", "ErrorRingKeeping", "CatalogueWarm", "PageSweepAtRoll", "KeepWarmAtRoll", "ShedStopper"];
        foreach (string concern in concerns)
        {
            var type = Host.GetType("TheYard.Api." + concern);
            Assert.True(type is not null && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type), $"{concern} is not a hosted service");
        }
    }
}
// #endregion slice-tests
