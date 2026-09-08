using System.Reflection;

namespace TheYard.Tests;

/// <summary>
/// A version the application was proven on, held by a test rather than by a
/// comment. Azure.Identity is the one with a history: the Cosmos DB project
/// arrived referencing 1.21.0, that reference pulled every project in the
/// graph up from the 1.17.1 the container had run on for a week, and
/// SqlClient's managed identity token wait overran its thirty seconds on
/// Container Instances, so the live site came up on files with the database
/// online (ADR: A second store on Cosmos DB, the addendum on the version
/// bump). The pin in the project file says why it is 1.17.1; this test says
/// so the day something moves it, in the gate rather than on the live site.
/// </summary>
public class PackagePinTests
{
    // #region pin
    [Fact]
    public void Azure_Identity_stays_on_the_version_the_container_was_proven_on()
    {
        // The one assembly every project resolves, whichever of them asked for
        // it: a transitive bump anywhere in the graph lands here. Loaded by
        // name rather than through a type, because Azure.Core 1.60 carries a
        // second copy of the credential types and naming one is ambiguous.
        Assembly assembly = Assembly.Load("Azure.Identity");
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "";

        Assert.StartsWith("1.17.1", version, StringComparison.Ordinal);
    }
    // #endregion pin
}
