using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The box's routes as they are mapped (ADR: The box reports itself): both posts refuse a missing or guessed key before
/// they read a body, say the one sentence wrong with a bad one, and hold a good one; the live read is anonymous and says
/// how long the box has been silent; and an accepted post stays out of the ring and the telemetry while a refused one
/// stays in.
/// </summary>
public sealed class BoxRoutesTests(BoxRoutesTests.KeyedHost host) : IClassFixture<BoxRoutesTests.KeyedHost>
{
    private const string Snapshot = "/api/admin/cloud/box/snapshot", Live = "/api/admin/cloud/box/live";

    /// <summary>The host with the operator's key set.</summary>
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        /// <summary>The operator's key on this host.</summary>
        public const string Key = "the-box-agent-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private async Task<HttpResponseMessage> Post(string route, object body, string? key = KeyedHost.Key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body, options: Wire) };
        if (key is not null)
        {
            request.Headers.Add(TheYard.Api.Shared.Auth.AdminKey.HeaderName, key);
        }
        return await host.CreateClient().SendAsync(request);
    }

    [Covers("POST", Snapshot, 401)]
    [Covers("POST", Live, 401)]
    [Fact]
    public async Task Without_the_key_both_posts_refuse_before_they_read_a_body()
    {
        Assert.Equal(401, (int)(await Post(Snapshot, BoxSamples.Snapshot(DateTimeOffset.UtcNow), key: null)).StatusCode);
        Assert.Equal(401, (int)(await Post(Snapshot, "not a snapshot", key: "a-guess")).StatusCode);
        Assert.Equal(401, (int)(await Post(Live, BoxSamples.Sample(DateTimeOffset.UtcNow), key: null)).StatusCode);
    }

    [Covers("POST", Snapshot, 400)]
    [Covers("POST", Live, 400)]
    [Fact]
    public async Task A_body_that_breaks_a_rule_is_refused_with_its_one_sentence()
    {
        var hostless = await Post(Snapshot, BoxSamples.Snapshot(DateTimeOffset.UtcNow) with { Host = "two words" });
        var crowded = await Post(Live, BoxSamples.Sample(DateTimeOffset.UtcNow) with { Cores = [.. Enumerable.Repeat(1.0, 65)] });
        var unread = await Post(Live, new[] { 1, 2, 3 });

        Assert.Equal(400, (int)hostless.StatusCode);
        Assert.Contains("host is 1 to 40 letters, digits or dashes.", await hostless.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("cores holds at most 64.", await crowded.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(400, (int)unread.StatusCode);
    }

    [Covers("POST", Snapshot, 200)]
    [Covers("POST", Live, 200)]
    [Fact]
    public async Task A_good_post_is_held_and_the_anonymous_live_read_gives_it_back_with_the_silence()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(200, (int)(await Post(Snapshot, BoxSamples.Snapshot(now.AddSeconds(-1)))).StatusCode);
        Assert.Equal(200, (int)(await Post(Live, BoxSamples.Sample(now.AddSeconds(-1)))).StatusCode);

        using var live = JsonDocument.Parse(await host.CreateClient().GetStringAsync(Live));
        using var card = JsonDocument.Parse(await host.CreateClient().GetStringAsync("/api/admin/cloud/box"));

        Assert.InRange(live.RootElement.GetProperty("silent_seconds").GetInt64(), 0, 30);
        Assert.Equal(2, live.RootElement.GetProperty("cadence_seconds").GetInt32());
        Assert.Equal([26.8, 30.9, 28.1, 27.1], live.RootElement.GetProperty("samples").EnumerateArray().Last().GetProperty("cores").EnumerateArray().Select(core => core.GetDouble()));
        Assert.Equal("WickedAwsome", card.RootElement.GetProperty("title").GetString());
        Assert.Equal(8, card.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void An_accepted_box_post_stays_out_of_the_ring_and_the_telemetry_and_a_refused_one_stays_in()
    {
        Assert.True(TheYard.Api.Shared.Activity.RequestRecorder.IsBoxPost("POST", Live, 200));
        Assert.False(TheYard.Api.Shared.Activity.RequestRecorder.IsBoxPost("POST", Live, 401));
        Assert.False(TheYard.Api.Shared.Activity.RequestRecorder.IsBoxPost("POST", Snapshot, 400));
        Assert.False(TheYard.Api.Shared.Activity.RequestRecorder.IsBoxPost("POST", "/api/admin/cloud/nightly/run", 200));
        Assert.Equal(BoxRoutes.Prefix, TheYard.Api.Shared.Activity.RequestRecorder.BoxPrefix);
        Assert.False(TelemetryRegistration.Traced(new PathString(Live)));
        Assert.False(TelemetryRegistration.Traced(new PathString(Snapshot)));
        Assert.True(TelemetryRegistration.Traced(new PathString("/api/admin/cloud/nightly/run")));
        Assert.True(TelemetryRegistration.Traced(new PathString("/api/vehicles")));
        Assert.True(TelemetryRegistration.Traced(PathString.Empty));
    }
}
