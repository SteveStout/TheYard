namespace JobHunt.Server.Application.Features.Status;

/// <summary>Finds the last run's card, today's or the newest of the week before.</summary>
/// <param name="share">The share.</param>
/// <param name="time">The clock.</param>
public sealed class LastCard(IShare share, TimeProvider time)
{
    private const int DaysBack = 7;

    /// <summary>The card, or a line saying there is none.</summary>
    /// <param name="ct">Stops the read.</param>
    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        DateOnly today = RunSlot.Day(time.GetUtcNow());
        for (int back = 0; back <= DaysBack; back++)
        {
            string path = $"{RunSlot.DayFolder(today.AddDays(-back))}/{RunSlot.Hour}-card.md";
            if (await share.ExistsAsync(path, ct))
            {
                return $"{path}\n{await share.ReadAsync(path, ct)}";
            }
        }
        return $"No card in the last {DaysBack} days.";
    }
}
