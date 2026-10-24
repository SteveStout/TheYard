// The one way this site's own code asks Claude anything (ADR: Every Claude lane reports its usage).
// Today nothing asks; the port exists so the first feature that does goes through the one client
// that writes a usage document from every response, and cannot forget to. OnionTests holds that
// the Anthropic API's address appears in the one adapter and nowhere else.
using TheYard.Domain.Shared.Usage;

namespace TheYard.Application.Shared.Claude;

// #region claude-client
/// <summary>One question for Claude.</summary>
/// <param name="Lane">The lane the usage document is kept under: the feature asking.</param>
/// <param name="Model">The model id.</param>
/// <param name="Prompt">The question.</param>
/// <param name="MaxTokens">The most the answer may run to.</param>
/// <param name="Run">The run id the usage document is kept under.</param>
public sealed record ClaudeAsk(string Lane, string Model, string Prompt, int MaxTokens, string Run);

/// <summary>Claude's answer, with the usage document that was written for it.</summary>
/// <param name="Text">The answer's text.</param>
/// <param name="Usage">What the call used, as kept.</param>
public sealed record ClaudeReply(string Text, UsageRecord Usage);

/// <summary>Port: asks Claude, and keeps a usage document for every answer.</summary>
public interface IClaudeClient
{
    /// <summary>Asks, keeps the usage document, and gives back the answer with it.</summary>
    Task<ClaudeReply> AskAsync(ClaudeAsk ask, CancellationToken cancellation);
}
// #endregion claude-client
