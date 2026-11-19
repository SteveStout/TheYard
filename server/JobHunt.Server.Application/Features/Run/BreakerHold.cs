namespace JobHunt.Server.Application.Features.Run;

/// <summary>The breaker's hold, as the morning reads it before each of its six lanes (the year-end plan, 7b step 3).</summary>
public static class BreakerHold
{
    /// <summary>
    /// The breaker's hold on a lane, read before it starts, as its held reason; null when there is none. A canary reads
    /// none, because proving a held lane works again is what it is for. A hold only ever stops a lane: an unreadable one
    /// holds it too.
    /// </summary>
    /// <param name="share">The share.</param>
    /// <param name="lane">The lane about to start.</param>
    /// <param name="ct">Stops the read.</param>
    public static async Task<string?> ReadAsync(IShare share, Lane lane, CancellationToken ct)
    {
        string path = RunSlot.BreakerHold(lane);
        if (!await share.ExistsAsync(path, ct))
        {
            return null;
        }
        string why;
        try
        {
            why = (await share.ReadAsync(path, ct)).Split('\n').FirstOrDefault(line => line.StartsWith("Reason: ", StringComparison.Ordinal))?["Reason: ".Length..].Trim() ?? "no reason line";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            why = "the hold file would not read";
        }
        return $"held by the breaker (J:\\{path.Replace('/', '\\')}): {why}";
    }
}
