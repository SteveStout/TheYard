using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheYard.Api;

namespace TheYard.Tests;

// #region shed-mount-tests
/// <summary>
/// The Shed inside TheYard's process, at its edges (ADR: One web app for every .NET process): a
/// Shed that will not start leaves the Yard names up, a Shed request that fails past its own
/// handler is a problem in TheYard's error record, The Shed's upload limit is The Shed's alone,
/// and The Shed is stopped after the server has drained and not before. Each case changes The
/// Shed's own builder through the addition this host hands it (ShedBuilderAddition), the way a
/// deploy's settings would.
/// </summary>
public sealed class ShedMountTests
{
    private const string Shed = "theshed.example";
    private const string Yard = "theyard.example";

    private static WebApplicationFactory<Program> Mounted(Action<WebApplicationBuilder> addition) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Hosts:Sql", Yard);
            builder.UseSetting("Hosts:Shed", Shed);
            builder.UseSetting("Shed:ContentRoot", Path.Combine(Repo.Root(), "samples", "theshed"));
            builder.ConfigureServices(services => services.AddSingleton(new ShedBuilderAddition(addition)));
        });

    private static void Settings(WebApplicationBuilder shed, Dictionary<string, string?> values) =>
        shed.Configuration.AddInMemoryCollection(values);

    // HTTPS, because The Shed in Development redirects plain HTTP, as it does on a developer's machine.
    private static HttpClient Client(WebApplicationFactory<Program> api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string host, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body };
        request.Headers.Add(SiteHosts.HeaderName, host);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task A_shed_that_throws_on_start_answers_503_and_the_yard_answers_200()
    {
        // An upload limit of nothing is a setting The Shed refuses as it starts (FilesRegistration.cs).
        await using var api = Mounted(shed => Settings(shed, new() { ["Files:MaxUploadBytes"] = "0" }));
        var client = Client(api);

        var shedAnswer = await Send(client, HttpMethod.Get, "/healthz", Shed);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, shedAnswer.StatusCode);
        Assert.Contains("The Shed is not running", await shedAnswer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, "/api/version", Yard)).StatusCode);
    }

    [Fact]
    public async Task A_shed_request_that_throws_past_its_handler_is_a_500_problem_in_the_yards_error_record()
    {
        await using var api = Mounted(shed => shed.Services.AddSingleton<IStartupFilter>(new ThrowsAhead("/boom")));
        var client = Client(api);

        var failed = await Send(client, HttpMethod.Get, "/boom", Shed);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Contains("The Shed could not answer", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var recorded = api.Services.GetRequiredService<ErrorRings>().Server.Snapshot();
        Assert.Contains(recorded, entry => entry.Path == "/boom" && entry.Status == 500 && entry.Message == nameof(InvalidOperationException));
        // The Yard's own names are untouched.
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, "/api/version", Yard)).StatusCode);
    }

    [Fact]
    public async Task An_upload_over_the_sheds_limit_is_refused_by_the_sheds_limit_on_the_sheds_name_only()
    {
        string home = Directory.CreateTempSubdirectory("shed-home-").FullName;
        try
        {
            // A one megabyte limit, so the form reader's ceiling is two; three megabytes is past it.
            await using var api = Mounted(shed => Settings(shed, new()
            {
                ["Files:MaxUploadBytes"] = (1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Files:AllowWrites"] = "true",
                ["Files:Home"] = home,
            }));
            var client = Client(api);

            HttpContent Upload()
            {
                var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(new byte[3 * 1024 * 1024]), "files", "big.bin");
                return form;
            }

            // The Shed's form reader stops at its own ceiling. Its controllers bind the form before
            // the action runs, so The Shed answers that refusal as a 400 problem naming the limit;
            // what this holds is whose limit it is: The Shed's, on The Shed's name, and nobody's on
            // a Yard name.
            var onShed = await Send(client, HttpMethod.Post, "/api/files/upload", Shed, Upload());
            string shedSaid = await onShed.Content.ReadAsStringAsync();
            Assert.True(onShed.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"{(int)onShed.StatusCode}: {shedSaid}");
            Assert.Contains("body length limit 2097152 exceeded", shedSaid, StringComparison.Ordinal);
            var onYard = await Send(client, HttpMethod.Post, "/api/files/upload", Yard, Upload());
            Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, onYard.StatusCode);
            Assert.DoesNotContain("body length limit", await onYard.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(home));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task The_shed_is_stopped_after_the_yard_begins_stopping_and_by_the_last_hosted_service()
    {
        var shedStop = new StopRecord();
        bool stoppedWhenTheYardBeganStopping = true;
        var api = Mounted(shed => shed.Services.AddHostedService(_ => new Watches(shedStop)));
        Client(api);
        var lifetime = api.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() => stoppedWhenTheYardBeganStopping = shedStop.Stopped);
        Assert.NotNull(api.Services.GetRequiredService<ShedSlot>().Shed);

        // The last of the host's own hosted services is the one that stops The Shed, so it is the
        // first of them to stop, and every one of them stops after the server has drained.
        var ours = api.Services.GetServices<IHostedService>()
            .Where(service => service.GetType().Assembly == typeof(ShedStopper).Assembly)
            .ToList();
        Assert.IsType<ShedStopper>(ours[^1]);

        await api.DisposeAsync();

        Assert.False(stoppedWhenTheYardBeganStopping, "The Shed was stopped before TheYard began stopping, which is before the drain");
        Assert.True(shedStop.Stopped, "The Shed was never stopped");
    }

    /// <summary>Whether The Shed's own host has stopped, written by a hosted service inside it.</summary>
    private sealed class StopRecord
    {
        public bool Stopped { get; set; }
    }

    /// <summary>A hosted service inside The Shed that writes down when The Shed stops.</summary>
    private sealed class Watches(StopRecord record) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            record.Stopped = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>A step in front of The Shed's whole pipeline, its exception handler included, that throws on one path.</summary>
    private sealed class ThrowsAhead(string path) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, inner) =>
            {
                if (context.Request.Path.Equals(path, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("thrown ahead of The Shed's handler");
                }
                await inner(context);
            });
            next(app);
        };
    }
}
// #endregion shed-mount-tests
