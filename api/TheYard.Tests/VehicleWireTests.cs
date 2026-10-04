using TheYard.Api;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The wire's vehicle (VehicleView) against the dataset's (Vehicle). The wire is built by hand in
/// VehicleWire.cs, so a field added to the dataset would never reach the browser and nothing else
/// would notice. Only the reserve amount stays on the server; the wire carries its state instead.
/// </summary>
public sealed class VehicleWireTests
{
    [Fact]
    public void Every_dataset_field_but_the_reserve_reaches_the_wire()
    {
        var wire = typeof(VehicleView).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var missing = typeof(Vehicle).GetProperties()
            .Select(property => property.Name)
            .Where(name => name != nameof(Vehicle.ReservePrice) && !wire.Contains(name))
            .ToList();
        Assert.Empty(missing);
        Assert.DoesNotContain(nameof(Vehicle.ReservePrice), wire);
    }
}
