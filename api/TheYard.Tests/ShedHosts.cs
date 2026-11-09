namespace TheYard.Tests;

// #region shed-hosts
/// <summary>
/// The test classes that start TheYard's host with The Shed mounted, which run one after another instead of side by
/// side. The Shed is a whole application of its own, built inside the host as it starts, and two such hosts starting
/// at the same moment in one test process crossed once on CI on main: SiteHostsTests' host failed to start on The
/// Shed's Files options check, the check ShedMountTests trips on purpose with Files:MaxUploadBytes 0, while
/// ShedMountTests started that host. Neither failed alone or in the gate. In one collection no two of them start a
/// host together. ShedHostsTests holds every class that mounts The Shed to it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ShedHosts
{
    /// <summary>The collection's name.</summary>
    public const string Name = "The hosts that mount The Shed";
}
// #endregion shed-hosts
