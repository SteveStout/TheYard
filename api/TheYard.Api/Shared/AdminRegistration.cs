using Microsoft.AspNetCore.Hosting.Server;
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// What the Admin tab reads that is not a ring: the proof's own client, the container's
/// Azure state, the peer, the proof and the page sweep, and the error rings and start time.
/// </summary>
public static class AdminRegistration
{
    /// <summary>Registers the proof, the Azure and peer readers, the page sweep, the error rings and the start time.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="relational">The relational store's contexts, whose quiet one the hourly reading reads.</param>
    /// <param name="build">The build, which the page sweep stamps its reading with.</param>
    /// <param name="identity">Where this process runs, for the Azure card's own reading.</param>
    public static void AddTheYardAdmin(this WebApplicationBuilder builder, RelationalContexts relational, BuildInfo build, HostIdentity identity)
    {
        // #region proof-clients
        // Where the performance proof sends its requests: this container's own
        // address, with cookies handled by hand because the proof holds one session
        // per store and a cookie jar would merge them (ADR: Same performance,
        // proven). The address is known only once the server is listening, so the
        // factory asks the server when a run starts (SelfAddress.cs); a test replaces
        // this registration with a client to its own in-memory server.
        builder.Services.AddSingleton(services => new ProofClients(() => new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(SelfAddress.Of(services.GetService<IServer>()) ?? "http://127.0.0.1:8080"),
            Timeout = TimeSpan.FromSeconds(60),
        }));
        // #endregion proof-clients

        // Identifiers, not secrets: the identity's client id and this web app's ARM path. The
        // template sets the path; the default is the one web app's.
        var azureSelf = new AzureSelf(
            AzureIds.ClientId(builder.Configuration),
            builder.Configuration["Azure:SelfResourceId"]
                ?? AzureResources.From(builder.Configuration).SitePath(AzureIds.SubscriptionId(builder.Configuration)),
            identity);
        builder.Services.AddSingleton(azureSelf);
        // The other container's metrics, read server side with a short patience (ADR: Backends, side by
        // side). Since 19 October no deploy sets Peer:Url: both sites are this process, and the card
        // says so in a sentence (ADR: One web app for every .NET process).
        var siteHosts = SiteSettings.From(builder.Configuration);
        var peer = new PeerReader(
            builder.Configuration["Peer:Url"],
            OutboundHttp.Client(PeerReader.Patience + TimeSpan.FromSeconds(1)),
            bothStoresHere: siteHosts.StoreSites.Count() > 1);
        builder.Services.AddSingleton(peer);
        // Once an hour, the certificate the site answers with and how full the relational store
        // is, for the Azure card (HourlyReadings.cs). Only a container with a public https address
        // reads a certificate; only one with Azure SQL Database reads a size.
        Uri? site = Uri.TryCreate(builder.Configuration["Site:Url"], UriKind.Absolute, out var siteUrl)
            && siteUrl.Scheme == Uri.UriSchemeHttps ? siteUrl : null;
        // #region hourly-readings-wiring
        // The reading is a delegate, which is the port for a one-call reading (ADR-080, the addendum
        // on the delegate). The quiet context is looked up at each reading, not here: a relational store that
        // refused at startup gets one only when the second chance attaches it later.
        builder.Services.AddSingleton(new HourlyReadings(site, cancellation => StorageStats.ReadAsync(relational.Quiet, cancellation), TimeProvider.System));
        // #endregion hourly-readings-wiring
        builder.Services.AddHostedService(services => services.GetRequiredService<HourlyReadings>());
        // The performance proof (ADR: Same performance, proven), built on first use so a test's own
        // ProofClients registration is the one it gets.
        builder.Services.AddSingleton(services => new ProofRunner(
            services.GetRequiredService<Backends>(),
            services.GetRequiredService<ProofClients>(),
            services.GetRequiredService<SqlRingBuffer>(),
            services.GetRequiredService<StoreRingBuffer>()));
        // The sweep over every address this container serves (ADR: Every page, checked at every roll):
        // it dials the loopback once the server is listening, and is started at the roll further down.
        builder.Services.AddSingleton(services => new PageStatusRunner(
            () => SelfAddress.Of(services.GetService<IServer>()) is { } dialable
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
            build.Version,
            build.Commit));

        // The two error rings and the moment the process started, registered before the
        // host exists so the endpoints can ask for them; the rings are wired to the kept
        // log and the start is marked when the host starts (Shared/Hosted/ErrorRingKeeping.cs).
        builder.Services.AddSingleton(new ErrorRings());
        builder.Services.AddSingleton(new SelfTestCooldown(TimeProvider.System));
        builder.Services.AddSingleton(new HostStart());
    }
}
