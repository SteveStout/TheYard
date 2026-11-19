using TheYard.Api.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The host's copy of the blog's answers (ADR: Every LinkedIn post is also a blog post, the addendum on the read model): readers who miss
/// together cost one read, a read that was under way when a save cleared the copy is never kept as the answer, and nothing to keep is kept
/// for nobody.
/// </summary>
public sealed class BlogAnswersTests
{
    private static readonly byte[] Old = "{\"title\":\"old\"}"u8.ToArray();
    private static readonly byte[] New = "{\"title\":\"new\"}"u8.ToArray();

    [Fact]
    public async Task Readers_who_miss_together_cost_the_database_one_read()
    {
        var answers = new BlogAnswers();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        async Task<byte[]?> Read(CancellationToken token)
        {
            Interlocked.Increment(ref reads);
            await gate.Task;
            return Old;
        }

        var readers = Enumerable.Range(0, 8).Select(_ => answers.GetAsync("list|", Read, CancellationToken.None)).ToList();
        gate.SetResult();
        var got = await Task.WhenAll(readers);

        Assert.Equal(1, reads);
        Assert.All(got, answer => Assert.Equal(Old, answer!.Json));
    }

    [Fact]
    public async Task A_read_under_way_when_a_save_clears_the_copy_is_not_kept_as_the_answer()
    {
        var answers = new BlogAnswers();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racing = answers.GetAsync("post|a-post", async _ =>
        {
            await gate.Task;
            return Old;
        }, CancellationToken.None);

        answers.Clear();
        gate.SetResult();
        Assert.Equal(Old, (await racing)!.Json);

        var after = await answers.GetAsync("post|a-post", _ => Task.FromResult<byte[]?>(New), CancellationToken.None);
        Assert.Equal(New, after!.Json);
    }

    [Fact]
    public async Task Nothing_to_keep_is_answered_as_nothing_and_read_again_next_time()
    {
        var answers = new BlogAnswers();
        int reads = 0;
        Task<byte[]?> Missing(CancellationToken token)
        {
            reads++;
            return Task.FromResult<byte[]?>(null);
        }

        Assert.Null(await answers.GetAsync("post|gone", Missing, CancellationToken.None));
        Assert.Null(await answers.GetAsync("post|gone", Missing, CancellationToken.None));
        Assert.Equal(2, reads);
    }
}
