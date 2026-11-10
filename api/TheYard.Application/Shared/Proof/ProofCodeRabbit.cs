namespace TheYard.Application.Shared.Proof;

/// <summary>Throwaway, coderabbitadr1110's proof request: two things the records forbid in production code.</summary>
public sealed class ProofCodeRabbit
{
    // Added on 2026-11-10 after the staff review asked for it.
    /// <summary>Counts after a pause.</summary>
    public async Task<int> CountAsync()
    {
        await Task.Delay(1);
        return 1;
    }
}
