using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// How the API's code is compiled and collected, and the reading that shows it on the live
/// site: /api/admin/metrics says how many methods the runtime compiled itself since the
/// process started and how long that took, beside the garbage collector's mode, so code
/// compiled ahead of time shows as fewer methods compiled at run time.
/// </summary>
public class CompilationTests(WebApplicationFactory<Program> host) : IClassFixture<WebApplicationFactory<Program>>
{
    // #region runtime-reading
    [Fact]
    public async Task The_metrics_say_what_the_runtime_compiled_and_how_it_collects()
    {
        var response = await host.CreateClient().GetAsync("/api/admin/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var metrics = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runtime = metrics.RootElement.GetProperty("runtime");
        // A host that has answered a request has compiled something at run time; under the
        // test runner nothing is compiled ahead of time, so this is never zero here.
        Assert.True(runtime.GetProperty("jit_methods").GetInt64() > 0);
        Assert.True(runtime.GetProperty("jit_ms").GetInt64() >= 0);
        Assert.Contains(runtime.GetProperty("gc").GetString(), new[] { "server", "workstation" });
        Assert.True(runtime.GetProperty("processors").GetInt32() >= 1);
    }
    // #endregion runtime-reading

    // #region compiled-ahead
    [Fact]
    public void The_image_publishes_the_api_compiled_ahead_of_time_for_the_platform_it_runs_on()
    {
        string dockerfile = File.ReadAllText(Path.Combine(Repo.Root(), "Dockerfile"));
        Assert.Contains("dotnet restore api/TheYard.Api/TheYard.Api.csproj -r linux-x64 -p:PublishReadyToRun=true", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-r linux-x64 --self-contained false -p:PublishReadyToRun=true -o /app/publish", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_runs_with_the_compilation_and_collection_its_project_asks_for()
    {
        // Read from the runtime configuration the build wrote, not from the project's text: a
        // property spelled wrong in the project is ignored without a warning, and the process
        // then runs on the SDK's default (the web SDK's is server collection).
        using var config = JsonDocument.Parse(File.ReadAllText(RuntimeConfig()));
        var settings = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.True(settings.GetProperty("System.Runtime.TieredCompilation").GetBoolean());
        Assert.True(settings.GetProperty("System.Runtime.TieredPGO").GetBoolean());
        Assert.False(settings.GetProperty("System.GC.Server").GetBoolean());
        Assert.True(settings.GetProperty("System.GC.Concurrent").GetBoolean());
    }

    /// <summary>The API's runtimeconfig.json beside the tests, or else the newest one its own build wrote.</summary>
    private static string RuntimeConfig()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "TheYard.Api.runtimeconfig.json");
        if (File.Exists(beside))
        {
            return beside;
        }
        string bin = Path.Combine(Repo.Root(), "api", "TheYard.Api", "bin");
        var built = Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, "TheYard.Api.runtimeconfig.json", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        Assert.True(built is not null, "the API has not been built, so there is no runtime configuration to read");
        return built!;
    }
    // #endregion compiled-ahead
}
