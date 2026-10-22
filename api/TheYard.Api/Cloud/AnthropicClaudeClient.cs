// The one adapter that calls the Anthropic API (ADR: Every Claude lane reports its usage): the
// Messages endpoint with the key configuration names (Claude:ApiKey, never in the repository), and
// a usage document kept from every answer's usage block before the answer is handed back, billed
// as api because a key is billed per token. Nothing calls it yet; OnionTests holds that the API's
// address is written here and nowhere else, so the first feature to ask Claude asks through it.
using System.Diagnostics;
using System.Text.Json;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Api;

/// <summary>Asks Claude over the Anthropic API and keeps a usage document for every answer.</summary>
/// <param name="key">The API key, from configuration; null or a placeholder leaves the client unable to ask.</param>
/// <param name="usage">Where the usage documents are kept.</param>
/// <param name="clock">This process's clock.</param>
public sealed class AnthropicClaudeClient(string? key, IUsageLog usage, TimeProvider clock) : IClaudeClient
{
    /// <summary>The Messages endpoint.</summary>
    public static readonly Uri Messages = new("https://api.anthropic.com/v1/messages");

    /// <summary>The API version the request is written for.</summary>
    public const string Version = "2023-06-01";

    private static HttpClient Http => OutboundHttp.Client(TimeSpan.FromSeconds(120));

    /// <summary>True when a key is configured.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(key) && !key.StartsWith("__", StringComparison.Ordinal);

    public async Task<ClaudeReply> AskAsync(ClaudeAsk ask, CancellationToken cancellation)
    {
        if (!Configured)
        {
            throw new InvalidOperationException("Claude:ApiKey is not configured.");
        }
        var started = clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, Messages)
        {
            Content = JsonContent.Create(new
            {
                model = ask.Model,
                max_tokens = ask.MaxTokens,
                messages = new[] { new { role = "user", content = ask.Prompt } },
            }),
        };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", Version);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var record = Usage(answer.RootElement, ask, started, watch.ElapsedMilliseconds);
        await usage.UpsertAsync(record, cancellation);
        return new ClaudeReply(Text(answer.RootElement), record);
    }

    /// <summary>The usage document an answer makes: its usage block's four counts, the wall time, billed as api.</summary>
    /// <param name="answer">The Messages endpoint's answer.</param>
    /// <param name="ask">The question it answers.</param>
    /// <param name="started">When the question was sent.</param>
    /// <param name="wallMs">How long the answer took.</param>
    public static UsageRecord Usage(JsonElement answer, ClaudeAsk ask, DateTimeOffset started, long wallMs)
    {
        long Count(string name) =>
            answer.TryGetProperty("usage", out var block) && block.TryGetProperty(name, out var value) && value.TryGetInt64(out long n) ? n : 0;
        string model = answer.TryGetProperty("model", out var named) && named.GetString() is { Length: > 0 } m ? m : ask.Model;
        return new UsageRecord(ask.Lane, UsageKind.Api, model, Count("input_tokens"), Count("cache_creation_input_tokens"), Count("cache_read_input_tokens"), Count("output_tokens"), wallMs, null, ask.Run, null, null, started);
    }

    /// <summary>The answer's text blocks, joined.</summary>
    /// <param name="answer">The Messages endpoint's answer.</param>
    public static string Text(JsonElement answer) =>
        answer.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Concat(content.EnumerateArray().Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text").Select(block => block.GetProperty("text").GetString()))
            : string.Empty;
}
