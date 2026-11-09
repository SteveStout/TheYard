using System.Text.Json.Nodes;

namespace JobHunt.Server.Application.Features.Probe;

/// <summary>
/// Proves the browser path from whatever process runs it, so it can be proven again after a Chrome or CLI update: a
/// cheap model opens its own tab on example.com, takes a saved screenshot, clicks once on empty page and closes the tab.
/// It goes nowhere near LinkedIn, and the answer is the model's own report plus the tools the CLI exposed.
/// </summary>
/// <param name="claude">The CLI.</param>
/// <param name="desktop">The desktop.</param>
/// <param name="folder">A local folder the probe's stream is kept in.</param>
public sealed class BrowserProbe(IClaudeCli claude, IDesktop desktop, string folder)
{
    private const string Model = "claude-haiku-5-5";
    private const string Chrome = "mcp__claude-in-chrome__";

    /// <summary>The probe's arguments.</summary>
    public static IReadOnlyList<string> Arguments() =>
    [
        "-p",
        "Call list_connected_browsers and report what it lists. Call select_browser with the deviceId of the one browser whose platform is Linux; if there is no such browser, stop here and report that. Call tabs_context_mcp with createIfEmpty true. Navigate that tab to https://example.com. Take a computer screenshot with save_to_disk true. Do one computer left_click at coordinate 5,5. Close the tab with tabs_close_mcp. Report the saved path and whether every step worked. Touch nothing else.",
        "--chrome", "--strict-mcp-config", "--model", Model, "--max-turns", "12", "--output-format", "stream-json", "--verbose",
        "--allowedTools", Chrome + "list_connected_browsers", Chrome + "select_browser", Chrome + "tabs_context_mcp", Chrome + "navigate", Chrome + "computer", Chrome + "tabs_close_mcp",
    ];

    /// <summary>Runs the probe; the exit is 0 when the CLI finished and the extension answered.</summary>
    /// <param name="ct">Stops the probe.</param>
    public async Task<(int Exit, string Output)> ExecuteAsync(CancellationToken ct)
    {
        if (await desktop.EnsureChromeAsync(ct) is { } down)
        {
            return (1, $"probe: {down}");
        }
        ClaudeRun run = await claude.RunAsync(new ClaudeRequest(Arguments(), folder, Path.Combine(folder, "stream.jsonl"), TimeSpan.FromMinutes(3)), ct);
        if (run.ResultLine is null)
        {
            return (1, $"probe: no result, claude exit {run.ExitCode}{(run.TimedOut ? ", timed out" : string.Empty)}; stream at {folder}/stream.jsonl");
        }
        JsonNode? result = JsonNode.Parse(run.ResultLine);
        bool failed = result?["is_error"]?.GetValue<bool>() != false || (result["permission_denials"]?.AsArray().Count ?? 0) > 0;
        return (failed ? 1 : 0, $"probe exit {run.ExitCode}: {result?["result"]}");
    }
}
