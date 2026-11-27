using JobHunt.Server.Infrastructure.Shared.Cli;

namespace JobHunt.Server.Tests;

/// <summary>
/// What a run's CLI inherits. The box's user units carry the one-year setup-token, and a CLI started with it loads no
/// claude-in-chrome server at all (init line "mcp_servers": []), so every browser run ended browser_down, the morning
/// invites canary first. A browser run goes on the claude.ai sign-in instead, as Remote
/// Control's unit does; a run without the browser keeps the token.
/// </summary>
public sealed class ClaudeCliTests : IDisposable
{
    private const string Echo = """echo "${CLAUDE_CODE_OAUTH_TOKEN:-none}|${ANTHROPIC_API_KEY:-none}|${ANTHROPIC_AUTH_TOKEN:-none}" """;

    private static readonly string[] Tokens = ["CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    private readonly string folder = Directory.CreateTempSubdirectory("server6am-cli-").FullName;

    private readonly Dictionary<string, string?> saved = Tokens.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    public ClaudeCliTests()
    {
        Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", "sk-ant-oat01-test");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "key");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "auth");
    }

    public void Dispose()
    {
        foreach ((string name, string? value) in saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        Directory.Delete(folder, recursive: true);
    }

    private async Task<string> SeenAsync(params string[] flags)
    {
        string stream = Path.Combine(folder, "stream.jsonl");
        await new ClaudeCli("/bin/sh").RunAsync(new ClaudeRequest(["-c", Echo, .. flags], folder, stream, TimeSpan.FromSeconds(20)), default);
        return (await File.ReadAllTextAsync(stream)).Trim();
    }

    [Fact]
    public async Task A_browser_run_drops_the_env_tokens_so_chrome_loads_on_the_claude_ai_sign_in()
    {
        Assert.Equal("none|none|none", await SeenAsync("--chrome", "--strict-mcp-config"));
    }

    [Fact]
    public async Task A_run_without_the_browser_keeps_the_token()
    {
        Assert.Equal("sk-ant-oat01-test|key|auth", await SeenAsync("--strict-mcp-config"));
    }
}
