using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

// #region test-catalogue
/// <summary>
/// The catalogue every test application boots with (ADR: The five-minute
/// gate).
///
/// <para>The site expands its two hundred seed vehicles to a hundred thousand
/// in memory at startup, and until this file existed so did every application
/// the suite booted: nineteen class fixtures and a few dozen per-test hosts,
/// each expanding a hundred thousand vehicles, building the search index over
/// them and holding all of it for the life of the class. Measured, the first
/// test of every class took twelve to twenty seconds, which was the boot, and
/// the suite's memory was the reason a two-minute run once became a
/// thirty-minute crawl.</para>
///
/// <para>A thousand is enough for every test that is not about the size: the
/// schedule spreads a thousand auctions across a week, so there are live,
/// upcoming and ended vehicles on every page, and every path the tests walk
/// is the same path. The one class that is about the size opts back in
/// through <see cref="FullCatalogue"/>. A module initializer rather than a
/// fixture, because it has to run before any host is built and the plain
/// <c>WebApplicationFactory&lt;Program&gt;</c> a class fixture hands out has
/// nowhere to say so; the setting is read by the same configuration the
/// container reads, so a developer who sets the variable themselves wins.</para>
/// </summary>
internal static class TestCatalogue
{
    public const int Vehicles = 1_000;

    [ModuleInitializer]
    internal static void Shrink()
    {
        if (Environment.GetEnvironmentVariable("Inventory__TargetCount") is null)
        {
            Environment.SetEnvironmentVariable("Inventory__TargetCount", Vehicles.ToString());
        }
    }
}

/// <summary>The site's own catalogue, a hundred thousand vehicles, for the tests that are about its size.</summary>
public sealed class FullCatalogue : WebApplicationFactory<Program>
{
    public const int Vehicles = 100_000;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.UseSetting("Inventory:TargetCount", Vehicles.ToString());
}

public class TestCatalogueTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// The number the whole suite's speed rests on, held here so a future
    /// change to how the host reads its configuration cannot quietly put a
    /// hundred thousand vehicles back into every test application.
    /// </summary>
    [Fact]
    public async Task A_test_application_boots_a_thousand_vehicles_and_the_full_catalogue_is_asked_for_by_name()
    {
        var client = factory.CreateClient();
        var page = await client.GetFromJsonAsync<JsonElement>("/api/vehicles?limit=1");
        Assert.Equal(TestCatalogue.Vehicles, page.GetProperty("total").GetInt32());

        // The schedule still puts every state on the page: the tests that walk
        // a live auction, and the ones that look for an ended or an upcoming
        // one, all find what they came for in a thousand.
        foreach (string status in new[] { "live", "upcoming", "ended" })
        {
            var some = await client.GetFromJsonAsync<JsonElement>($"/api/vehicles?status={status}&limit=1");
            Assert.InRange(some.GetProperty("total").GetInt32(), 1, TestCatalogue.Vehicles - 1);
        }
    }
}
// #endregion test-catalogue
