// The performance proof starts here: the runner's state, its limits, and how a run starts in
// the background. The rest of the runner and the types it hands back live beside it:
//   ProofRunner.Rounds.cs   - one run, the accounts it signs in with and its paired rounds
//   ProofRunner.Requests.cs - one timed request, and reading what each answer says
//   ProofClients.cs         - where the proof's requests go
//   ProofResult.cs          - the result, its rows and cells, and the verdict on each row
namespace TheYard.Api;

// #region proof
/// <summary>
/// The performance proof (ADR: Same performance, proven): the same requests a
/// visitor makes, sent by this container to itself, once per store per round,
/// in paired rounds that alternate which store goes first. Identical process,
/// identical request, identical request ring; only the store differs, and the
/// round trip to each store is measured on its own so the difference the
/// stores make can be told from the difference their distance makes.
///
/// <para>It runs in the background and keeps its last result, because eight
/// rounds of eight requests on two stores is half a minute, and an Admin tab
/// that hung for half a minute would be measuring the wrong thing. One run
/// at a time: a second request to start while one is running is told so.</para>
/// </summary>
public sealed partial class ProofRunner(
    Backends backends,
    ProofClients clients,
    SqlRingBuffer sqlLog,
    StoreRingBuffer storeLog)
{
    /// <summary>How many paired rounds a run takes when the caller does not say.</summary>
    public const int DefaultRounds = 8;

    /// <summary>The most paired rounds one run may take; a larger request is clamped to this.</summary>
    public const int MaxRounds = 20;

    /// <summary>The paths a round takes, in the order a visitor takes them, with the label the card shows.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Paths =
    [
        ("sign_in", "Sign in"),
        ("listing", "Listing page"),
        ("vehicle", "Vehicle page"),
        ("facets", "Filter values"),
        ("bid", "Bid write"),
        ("raise", "Bid raise"),
        ("reset", "Reset"),
    ];

    /// <summary>
    /// How long after one run the next may start. Starting a run is a write
    /// and needs a signed-in visitor (ADR: The one write a stranger can make,
    /// addendum), and the accounts a run bids with are made once per process
    /// and reused, so a loop of starts costs the site nothing but sixteen bids
    /// a minute on vehicles the proof picks; the minute is what keeps the card
    /// from being asked to prove the same thing twice at once.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    /// <summary>The one-at-a-time gate: a run holds it from start to finish.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The result of the last run that finished or failed, or null before the first.</summary>
    private ProofResult? _last;

    /// <summary>Whether a run is on right now; read from request threads while the run's own thread writes it.</summary>
    private volatile bool _running;

    // #region proof-accounts
    /// <summary>
    /// The proof's own accounts, one per store, made on the first run this
    /// process performs and kept for the rest of its life: the address, the
    /// password it was registered with, and the registration's own sample, so
    /// later runs still show what registering cost without registering again.
    /// Registering two fresh accounts on every run would make an anonymous
    /// loop of one start a minute use exactly the hour's allowance of
    /// registrations (ADR: The one write a stranger can make, addendum). The
    /// password is random per process and lives nowhere but here.
    /// </summary>
    private readonly Dictionary<string, ProofAccount> _accounts = new(StringComparer.Ordinal);

    /// <summary>The password every proof account is registered and signed in with, random for each process.</summary>
    private readonly string _password = "proof-" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    /// <summary>One proof account kept for the life of the process.</summary>
    /// <param name="Email">The address the account was registered with.</param>
    /// <param name="Registration">The timed sample of its registration, shown again on every later run.</param>
    private sealed record ProofAccount(string Email, Sample Registration);
    // #endregion proof-accounts

    /// <summary>Whether a run is on right now.</summary>
    public bool Running => _running;

    /// <summary>The last run's result, or null when no run has finished yet.</summary>
    public ProofResult? Last => _last;

    /// <summary>What the page reads: whether a run is on, and the last result if there is one.</summary>
    public object Status => new
    {
        status = _running ? "running" : _last is null ? "idle" : _last.Status,
        result = _last,
    };

    /// <summary>Start a run in the background; false when one is already running or the last one finished less than a minute ago.</summary>
    public bool TryStart(int rounds)
    {
        if (_last?.FinishedAt is { } finished && DateTimeOffset.UtcNow - finished < Cooldown)
        {
            return false;
        }
        if (!_gate.Wait(0))
        {
            return false;
        }

        _running = true;
        _ = Task.Run(async () =>
        {
            try
            {
                _last = await RunAsync(rounds);
            }
            catch (Exception ex)
            {
                // The type, not the message: this result is served on a public
                // page, and an exception message can carry a host name.
                _last = ProofResult.Failed($"the proof stopped on {ex.GetType().Name}", backends);
            }
            finally
            {
                _running = false;
                _gate.Release();
            }
        });
        return true;
    }

    /// <summary>One store's side of the run: its account, its cookie, and every sample taken on it.</summary>
    public sealed class StoreRun(Backend backend)
    {
        /// <summary>The store this side of the run talks to.</summary>
        public Backend Backend { get; } = backend;

        /// <summary>The session cookie the store's account signed in with, sent on every request after; null before sign-in.</summary>
        public string? Cookie { get; set; }

        /// <summary>The address of the proof account on this store.</summary>
        public string Email { get; set; } = "";

        /// <summary>The median round trip to the store, in milliseconds, or null when it was not measured.</summary>
        public long? HopMs { get; set; }

        /// <summary>Every timed request taken on this store, in the order they were taken.</summary>
        public List<Sample> Samples { get; } = [];
    }

    /// <summary>One timed request in a proof run, and what the store did to answer it.</summary>
    /// <param name="Path">The key of the path the request took, such as sign_in or listing.</param>
    /// <param name="Ms">How long the request took, body included, in milliseconds.</param>
    /// <param name="Status">The HTTP status code it answered with.</param>
    /// <param name="Operations">How many statements or store operations the request ran.</param>
    /// <param name="RequestUnits">The request units the operations spent; 0 on a store with no such unit.</param>
    public sealed record Sample(string Path, long Ms, int Status, int Operations, double RequestUnits);
}
// #endregion proof
