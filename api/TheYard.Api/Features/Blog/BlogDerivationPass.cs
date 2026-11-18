// The pass that derives the blog's posts again after a roll (ADR: Every LinkedIn post is also a blog post, the
// addendum on the read model). A post's drawn body, words and keywords are a copy of the rules' answer, stamped with
// the rules' version; when a roll brings new rules, or a post was saved before there were any, this pass works them
// out again and drops the host's kept answers, so nobody has to save fifteen posts by hand. The first time a post is
// derived it takes its topics from data/blog/topics.json, the backfill the owner reviewed in the mockup.
using System.Text.Json;
using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

/// <summary>Derives the stale posts once the blog's store can be written, then stops.</summary>
/// <param name="handler">The command that derives the stale posts, over the blog's one store.</param>
/// <param name="paths">Where the backfill file is.</param>
/// <param name="answers">The host's kept answers, dropped once the posts are derived.</param>
/// <param name="logger">Where a failed pass is said.</param>
public sealed class BlogDerivationPass(DerivePostsHandler handler, HostPaths paths, BlogAnswers answers, ILogger<BlogDerivationPass> logger) : BackgroundService
{
    /// <summary>How long to wait before asking a store that cannot be written yet again.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>The most times it asks before it leaves the posts to the next roll.</summary>
    public const int Tries = 60;

    /// <summary>Runs the pass in the background.</summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunAsync(DeriveOnceAsync, Every, Tries, Task.Delay, logger, stoppingToken);

    /// <summary>
    /// The loop, on its own so a test can run it with no waiting: derive, and stop on the first answer that is not
    /// null; on a store that cannot be written, or a pass that throws, wait and try again, at most so many times.
    /// Returns how many posts the pass derived, or null when every try came back empty.
    /// </summary>
    public static async Task<int?> RunAsync(Func<CancellationToken, Task<int?>> derive, TimeSpan every, int tries, Func<TimeSpan, CancellationToken, Task> wait, ILogger logger, CancellationToken cancellation)
    {
        for (int i = 0; i < tries && !cancellation.IsCancellationRequested; i++)
        {
            try
            {
                if (await derive(cancellation) is { } derived)
                {
                    logger.LogInformation("Blog derivation pass: {Count} posts derived under the rules' current version", derived);
                    return derived;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Blog derivation pass failed; it tries again in {Every}", every);
            }
            await wait(every, cancellation);
        }
        return null;
    }

    private async Task<int?> DeriveOnceAsync(CancellationToken cancellation)
    {
        var derived = await handler.HandleAsync(new DerivePostsCommand(await TopicsAsync(paths.BlogTopicsPath, cancellation)), cancellation);
        if (derived is > 0)
        {
            answers.Clear();
        }
        return derived;
    }

    /// <summary>The backfill's topics by slug; empty when the file is not there.</summary>
    /// <param name="path">data/blog/topics.json.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> TopicsAsync(string path, CancellationToken cancellation)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }
        await using var file = File.OpenRead(path);
        var read = await JsonSerializer.DeserializeAsync<Dictionary<string, string[]>>(file, cancellationToken: cancellation) ?? [];
        return read.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value, StringComparer.Ordinal);
    }
}
