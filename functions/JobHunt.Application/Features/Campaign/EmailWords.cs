namespace JobHunt.Application.Features.Campaign;

/// <summary>
/// The sixty words a Track A email runs to at most (ADR-075's row), counted as LeaderTemplateTests counts them: every
/// line but the bare link, split on spaces.
/// </summary>
public static class EmailWords
{
    /// <summary>The most words a Track A email may run to, its bare link not counted.</summary>
    public const int Max = 60;

    /// <summary>The email's words, its bare link lines left out.</summary>
    public static int Count(string text) =>
        text.Split('\n').Where(line => !line.StartsWith("https://", StringComparison.Ordinal)).Sum(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
}
