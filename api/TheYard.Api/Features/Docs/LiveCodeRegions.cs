// The code-regions port answered by the live-sample reader (ADR-014), from the repository files this build
// carries, at its commit.
namespace TheYard.Api.Features.Docs;

/// <summary>One marked region read by LiveSamples.</summary>
/// <param name="repoRoot">The repository's root as this build carries it.</param>
/// <param name="commit">The commit the build was made from.</param>
public sealed class LiveCodeRegions(string repoRoot, string commit) : ICodeRegions
{
    public string Read(string path, string region) => LiveSamples.Expand($"```live path={path} region={region}\n```", repoRoot, commit);
}
