namespace TheYard.Tests;

/// <summary>Throwaway, coderabbitadr1110's proof request: an async test method with no token, which the record allows.</summary>
public class ProofCodeRabbitTests
{
    /// <summary>A test method that awaits and takes no CancellationToken.</summary>
    [Fact]
    public async Task An_async_test_with_no_token()
    {
        await Task.Yield();
        Assert.True(true);
    }
}
