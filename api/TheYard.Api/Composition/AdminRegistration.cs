namespace TheYard.Api;

/// <summary>
/// What the Admin tab reads that is not a ring: the proof's own client, the container's
/// Azure state, the peer, the proof and the page sweep, and the error rings and start time.
/// </summary>
public static class AdminRegistration
{
    /// <summary>Registers the proof, the Azure and peer readers, the page sweep, the error rings and the start time.</summary>
    public static void AddTheYardAdmin(this WebApplicationBuilder builder, YardComposition host)
    {
        var backends = host.Backends;
        var sqlLog = host.SqlLog;
        var storeLog = host.StoreLog;
        string buildVersion = host.Build.Version;
        string buildCommit = host.Build.Commit;
        // #region proof-clients
        // Where the performance proof sends its requests: this container's own
        // address, with cookies handled by hand because the proof holds one session
        // per store and a cookie jar would merge them (ADR: Same performance,
        // proven). The address is known only once the server is listening, so the
        // factory reads it when a run starts; a test replaces this registration with
        // a client to its own in-memory server.
        builder.Services.AddSingleton(new ProofClients(() => new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(host.SelfUrl ?? "http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(60),
        }));
        // #endregion proof-clients

        // Identifiers, not secrets: the identity's client id and this group's ARM path.
        var azureSelf = new AzureSelf(
            builder.Configuration["Azure:ClientId"] ?? "2888a6ca-be1c-46a5-a1de-c666b1d193e5",
            builder.Configuration["Azure:SelfResourceId"]
                ?? "/subscriptions/df3b718c-6d99-4904-8102-6f865941f640/resourceGroups/RG-THEYARD-SS/providers/Microsoft.ContainerInstance/containerGroups/aci-theyard-ss");
        builder.Services.AddSingleton(azureSelf);
        // The other container's metrics, read server side with a short patience (ADR: Backends, side by side).
        var peer = new PeerReader(
            builder.Configuration["Peer:Url"],
            new HttpClient { Timeout = PeerReader.Patience + TimeSpan.FromSeconds(1) });
        builder.Services.AddSingleton(peer);
        // The performance proof (ADR: Same performance, proven), built on first use so a test's own
        // ProofClients registration is the one it gets.
        builder.Services.AddSingleton(services => new ProofRunner(backends, services.GetRequiredService<ProofClients>(), sqlLog, storeLog));
        // The sweep over every address this container serves (ADR: Every page, checked at every roll):
        // it dials the loopback once the server is listening, and is started at the roll further down.
        builder.Services.AddSingleton(services => new PageStatusRunner(
            () => SelfAddress.Of(services) is { } dialable
                ? new HttpClient(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(dialable),
                    Timeout = TimeSpan.FromSeconds(30),
                }
                : null,
            // The frontend is in the image and not in a checkout, so the addresses it
            // serves are checked where they exist and named nowhere else.
            () => services.GetRequiredService<IWebHostEnvironment>() is { } environment
                && !string.IsNullOrEmpty(environment.WebRootPath)
                && File.Exists(Path.Combine(environment.WebRootPath, "index.html")),
            buildVersion,
            buildCommit));

        // The two error rings and the moment the process started, registered before the
        // host exists so the endpoints can ask for them; the rings are wired to the kept
        // log and the start is marked once the host is up (Composition/Startup.cs).
        var errorRings = new ErrorRings();
        builder.Services.AddSingleton(errorRings);
        var hostStart = new HostStart();
        builder.Services.AddSingleton(hostStart);
        host.ErrorRings = errorRings;
        host.Start = hostStart;
    }
}
