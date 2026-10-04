using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// What the composition root's steps hand one another: each step reads what the
/// steps before it made and records what it makes, so Program.cs can list the
/// steps in order and each one can live in a file of its own. Filled once, at
/// startup, and never read by a request except through what was registered.
/// </summary>
public sealed class YardComposition
{
    /// <summary>Where this process finds the files it serves.</summary>
    public required HostPaths Paths { get; init; }

    /// <summary>The version and commit baked into this build (ADR-005).</summary>
    public required BuildInfo Build { get; init; }

    /// <summary>The configured relational connection string, or null for a scratch database.</summary>
    public string? ConfiguredDatabase { get; set; }

    /// <summary>The scratch SQLite file this process deletes on the way out when nothing was configured.</summary>
    public string ScratchDatabase { get; set; } = "";

    /// <summary>The relational store: SQL Server when the deploy gave one, SQLite otherwise.</summary>
    public YardConnection Relational { get; set; } = null!;

    /// <summary>The document store's operations, for the Admin tab.</summary>
    public StoreRingBuffer StoreLog { get; set; } = null!;

    /// <summary>The relational store's statements, for the Admin tab.</summary>
    public SqlRingBuffer SqlLog { get; set; } = null!;

    /// <summary>The accessor both stores and the log providers read the current request through.</summary>
    public HttpContextAccessor HttpContextAccessor { get; set; } = null!;

    /// <summary>The request describer the store logs file each operation under.</summary>
    public HttpCurrentRequest CurrentRequest { get; set; } = null!;

    /// <summary>The relational backend, on the files until its store is attached.</summary>
    public Backend SqlBackend { get; set; } = null!;

    /// <summary>The document store, when there is an account to talk to.</summary>
    public CosmosStore? Cosmos { get; set; }

    /// <summary>Every store this container runs, and the default one.</summary>
    public Backends Backends { get; set; } = null!;

    /// <summary>The host's loggers, attached once the host exists; read by a store attached later.</summary>
    public ILoggerFactory? HostLoggers { get; set; }

    /// <summary>The raw log lines, for the Admin tab.</summary>
    public LogRingBuffer LogLog { get; set; } = null!;

    /// <summary>The recent requests, for the timing card.</summary>
    public RequestRingBuffer RequestLog { get; set; } = null!;

    /// <summary>The kept log's collector (ADR: Logs that outlive the container).</summary>
    public LogCollector LogCollector { get; set; } = null!;

    /// <summary>What offers each ring's entries to the kept log.</summary>
    public KeptRingWriter KeptRings { get; set; } = null!;

    /// <summary>The keep-warm loop's slot, filled once the host is built (ADR: Kept awake).</summary>
    public KeepWarmState KeepWarm { get; set; } = null!;

    /// <summary>This site as a visitor reaches it, when configured.</summary>
    public string? SiteUrl { get; set; }

    /// <summary>The request hook: files one request in the ring, the activity card and the kept log.</summary>
    public Action<HttpContext, TimeSpan> RecordRequest { get; set; } = (_, _) => { };

    /// <summary>The signing key as configured, or null when this process invented one.</summary>
    public string? ConfiguredSigningKey { get; set; }

    /// <summary>The key sessions are signed with.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>The session issuer.</summary>
    public TokenIssuer Tokens { get; set; } = null!;

    /// <summary>The visitor tokens, keyed with the signing key.</summary>
    public VisitorTokens? VisitorTokens { get; set; }

    /// <summary>The activity collector, resolved once the host is built.</summary>
    public ActivityCollector? ActivityCollector { get; set; }

    /// <summary>This container's own address, known once the server is listening; the proof dials it.</summary>
    public string? SelfUrl { get; set; }

    /// <summary>The server's and the browser's error rings.</summary>
    public ErrorRings ErrorRings { get; set; } = null!;

    /// <summary>When the process finished starting.</summary>
    public HostStart Start { get; set; } = null!;

    /// <summary>
    /// The managed identity's client id: the deploy's setting when it gives one, otherwise the
    /// identity both web apps run as. Read here once so the five steps that sign in to Azure
    /// cannot disagree about who they are.
    /// </summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string AzureClientId(IConfiguration configuration) =>
        configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5";

    /// <summary>The subscription the site's resources live in, from the deploy's setting or the default.</summary>
    /// <param name="configuration">The app's configuration.</param>
    public static string AzureSubscriptionId(IConfiguration configuration) =>
        configuration["Azure:SubscriptionId"] ?? "df3b718c-6d99-4904-8102-6f865941f640";
}
