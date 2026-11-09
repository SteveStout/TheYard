namespace TheYard.Tests;

/// <summary>
/// Every test class that mounts The Shed (it sets Shed:ContentRoot) is in the ShedHosts collection, so a new one
/// joins it in the commit that writes it rather than racing the others on a busy runner.
/// </summary>
public sealed class ShedHostsTests
{
    [Fact]
    public void Every_class_that_mounts_the_shed_runs_in_the_shed_hosts_collection()
    {
        string folder = Path.Combine(Repo.Root(), "api", "TheYard.Tests");
        var mounting = Directory.EnumerateFiles(folder, "*.cs")
            .Where(file => Path.GetFileName(file) != "ShedHostsTests.cs")
            .Where(file => File.ReadAllText(file).Contains("UseSetting(\"Shed:ContentRoot\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(mounting.Count >= 2, "the scan found fewer classes that mount The Shed than are known to");
        Assert.All(mounting, file => Assert.Contains("[Collection(ShedHosts.Name)]", File.ReadAllText(file), StringComparison.Ordinal));
    }
}
