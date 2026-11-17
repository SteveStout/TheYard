using Mono.Cecil;
using TheYard.Data;

namespace TheYard.Tests;

// #region data-records
/// <summary>
/// Data holds the rules' records and nothing else (ADR: Onion and SOLID, how this codebase holds
/// them). It is the innermost ring, the one every other ring reads, so a shape put there is one no
/// ring can be kept from. OnionTests holds what Data references and that it has no behaviour; a ring
/// can obey both and still fill with report shapes, which is what this reads: every type Data
/// declares is on the list below with why it is there, and every one is a record a Domain rule reads.
/// A shape only a slice reads lives in that slice; a shape a Domain rule validates and nothing else
/// in Domain does lives beside that rule, in the rule's own slice of Domain.
/// </summary>
public sealed class DataRecordsTests
{
    /// <summary>
    /// The types Data may declare, each with why. A type that is not here fails the test, and so does
    /// an entry whose type no longer exists, so the list cannot outlive what it names.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["TheYard.Data.Vehicle"] = "a listing exactly as data/vehicles.json has it: the bid, standing, schedule and search rules read it, and every ring passes it",
        ["TheYard.Data.PhotoEntry"] = "one photo of the stock-photo manifest as the file has it: PhotoGallery picks a vehicle's photos from it, and the manifest's sources fill it",
    };

    [Fact]
    public void Data_declares_only_the_records_on_its_list()
    {
        var declared = Declared();
        var wrong = declared.Where(type => !Allowed.ContainsKey(type)).Select(type => $"{type} is in Data and not on its list; a shape only a slice reads lives in that slice").ToList();
        wrong.AddRange(Allowed.Keys.Where(type => !declared.Contains(type)).Select(type => $"{type} is on Data's list and Data no longer declares it"));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_record_in_Data_is_one_a_Domain_rule_reads()
    {
        using var domain = ModuleDefinition.ReadModule(typeof(BidRules).Assembly.Location);
        var read = domain.GetTypeReferences().Select(reference => reference.FullName).ToHashSet(StringComparer.Ordinal);
        Assert.All(Allowed.Keys, type => Assert.True(read.Contains(type), $"{type} is in Data and no Domain type reads it"));
    }

    /// <summary>Every type the Data assembly declares in TheYard's namespaces, leaving out what the compiler writes.</summary>
    private static HashSet<string> Declared()
    {
        using var module = ModuleDefinition.ReadModule(typeof(Vehicle).Assembly.Location);
        return module.GetTypes()
            .Where(type => type.Namespace.StartsWith("TheYard", StringComparison.Ordinal) && !type.Name.Contains('<', StringComparison.Ordinal))
            .Where(type => !type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute"))
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }
}
// #endregion data-records
