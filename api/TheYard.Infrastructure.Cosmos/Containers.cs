// The catalog of containers: every container name this store uses and the partition key each
// one is built on. Its own file because every adapter and the startup check read it, and it is
// the one place to compare against the definitions in infra/cosmos.
namespace TheYard.Infrastructure.Cosmos;

// #region containers
/// <summary>
/// The containers this store uses and the partition key each one is built on.
/// The authority is `infra/cosmos/<name>.json`, which a person applies with the
/// Azure CLI; this catalog is what the adapters use, and a test holds the two
/// together (ADR: The partition key). Why the definition and not the code is
/// the authority is the addendum to ADR: Data first, and the database in source
/// control.
/// </summary>
public static class Containers
{
    /// <summary>The seed vehicles, partitioned on the make.</summary>
    public const string Vehicles = "vehicles";

    /// <summary>The photo manifest, partitioned on the body style.</summary>
    public const string Photos = "photos";

    /// <summary>Every buyer's standing on every vehicle, partitioned on the buyer.</summary>
    public const string Bids = "bids";

    /// <summary>The accounts and the email claims, partitioned on the document id.</summary>
    public const string Users = "users";

    /// <summary>The experiment: the 100,000 expanded vehicles, under the tuned indexing policy.</summary>
    public const string Catalogue = "catalogue";

    /// <summary>The same 100,000 under the default policy, seeded once so the default's cost is measured.</summary>
    public const string CatalogueDefault = "catalogue-default";

    /// <summary>Site activity: hour counters and visitor counters, partitioned on the UTC day (ADR: Site activity, and the line an address does not cross).</summary>
    public const string Activity = "activity";

    /// <summary>The kept log: one document per request, error or warning, partitioned on the UTC day, expiring by the container's time-to-live (ADR: Logs that outlive the container).</summary>
    public const string Logs = "logs";

    /// <summary>Password reset links: one document per link under the GUID the link carries, expiring after the hour by the container's time-to-live, deleted on use (ADR: Accounts and per-user bids).</summary>
    public const string Resets = "resets";

    /// <summary>What the machines were doing: one document a minute from each site, partitioned on the UTC day, expiring after a month by the container's time-to-live (ADR: What the machines are doing).</summary>
    public const string Machines = "machines";

    /// <summary>Container name to partition key path, exactly as the definition files declare them.</summary>
    public static readonly IReadOnlyDictionary<string, string> PartitionKeyPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Vehicles] = "/make",
        [Photos] = "/style",
        [Bids] = "/user_id",
        [Users] = "/id",
        [Catalogue] = "/make",
        [CatalogueDefault] = "/make",
        [Activity] = "/day",
        [Logs] = "/day",
        [Resets] = "/id",
        [Machines] = "/day",
    };

    /// <summary>
    /// The four the site cannot come up without. The experiment containers are
    /// optional: a container that is missing or empty makes the experiment card
    /// say so, and changes nothing about the site (ADR: The partition key). The
    /// activity container is optional the same way: missing, the Admin tab's
    /// activity card says so and nothing is kept. So is the logs container,
    /// and so is the resets container: missing, minting a link fails and
    /// says so, and everything else on the site is untouched.
    /// </summary>
    public static readonly IReadOnlyList<string> Required = [Vehicles, Photos, Bids, Users];
}
// #endregion containers
