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
}
