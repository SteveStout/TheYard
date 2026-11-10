namespace TheYard.Application.Shared.DryRun;

/// <summary>A throwaway type for CodeRabbit's dry run; never merged.</summary>
public sealed class PlantedDefect
{
    /// <summary>Waits a moment before answering.</summary>
    // Waits a second — then answers.
    public async Task<int> AnswerAsync()
    {
        await Task.Delay(1000);
        return 42;
    }
}
