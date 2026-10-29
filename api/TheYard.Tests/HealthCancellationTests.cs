using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The health endpoints hand the request its own cancellation down to the store
/// (ADR: The ports learn to wait, the addendum on the token). The report's
/// database check is two point reads over the network, and it is polled by the
/// orchestrator and by every deploy, so it is the one health check that can
/// outlive the caller who asked for it: a poll that timed out at three seconds
/// left the container paying for the read for as long as the store took. These
/// tests hold the thread rather than the timing, because a token that arrives is
/// a token a later version can act on and a token that was dropped is not.
/// </summary>
public class HealthCancellationTests
{
    // #region health-token
    /// <summary>
    /// The report's store check is handed a token that can be cancelled, which is
    /// the request's. A version that passed <c>CancellationToken.None</c>, or took
    /// no token at all, hands down a token whose <c>CanBeCanceled</c> is false, and
    /// no amount of timing out upstream would ever stop the read.
    /// </summary>
    [Fact]
    public async Task The_health_report_hands_the_store_probe_a_token_the_request_can_cancel()
    {
        var seen = new ProbeRecord();
        using var host = HostWithProbe(seen);

        var answer = await host.CreateClient().GetAsync("/api/health");

        Assert.True(answer.IsSuccessStatusCode, $"/api/health answered {(int)answer.StatusCode}");
        Assert.True(seen.Asked, "the store probe was never run, so this test proves nothing about its token");
        Assert.True(seen.Token.CanBeCanceled, "the probe was handed a token nobody can cancel, so the request's own cancellation was dropped on the way down");
    }

    /// <summary>
    /// Readiness runs only the checks that gate it, and the store is not one of
    /// them, so its probe is never reached. Held here beside the report's test so
    /// that threading the token through readiness cannot quietly start a network
    /// read on the path the deploy and the orchestrator poll every few seconds.
    /// </summary>
    [Fact]
    public async Task Readiness_still_never_asks_the_store()
    {
        var seen = new ProbeRecord();
        using var host = HostWithProbe(seen);

        var answer = await host.CreateClient().GetAsync("/readyz");

        Assert.True(answer.IsSuccessStatusCode, $"/readyz answered {(int)answer.StatusCode}");
        Assert.False(seen.Asked, "readiness reached the store, which is the read the fallback exists to avoid");
    }
    // #endregion health-token

    /// <summary>What the probe was handed, so the test can read it after the request has gone.</summary>
    private sealed class ProbeRecord
    {
        public bool Asked { get; set; }

        public CancellationToken Token { get; set; }
    }

    /// <summary>
    /// The real host with one store whose probe records the token it was given.
    /// Backends is a singleton, so a registration added after the host's own is the
    /// one the health endpoint resolves.
    /// </summary>
    private static WebApplicationFactory<Program> HostWithProbe(ProbeRecord record)
    {
        var backend = new Backend
        {
            Key = "sql",
            Name = "SQLite",
            Database = new DatabaseState(true, "SQLite"),
            Startup = new StartupTimings(),
            Inventory = new Stock(new NoVehicles(), new NoPhotos()),
            Bids = new BidBook(new MemoryBidStore()),
            Market = new Room(new Random(7)),
            Probe = cancellation =>
            {
                record.Asked = true;
                record.Token = cancellation;
                return Task.FromResult(true);
            },
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton(new Backends([backend], "sql"))));
    }

    private sealed class NoVehicles : IVehicleSource
    {
        public Task<IReadOnlyList<Vehicle>> LoadAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<Vehicle>>([]);
    }

    private sealed class NoPhotos : IPhotoManifestSource
    {
        public Task<IReadOnlyList<PhotoEntry>> LoadAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
    }
}
