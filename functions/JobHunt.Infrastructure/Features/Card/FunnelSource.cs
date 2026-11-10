namespace JobHunt.Infrastructure.Features.Card;

/// <summary>
/// Reads what one day's funnel is counted from (FunnelCount): the tracker master, campaign/pass.log, the day's JOBS
/// file, the leaders lane's OUTREACH-DRAFTS.md for the day, and every LinkedIn spool file the 6 AM program left,
/// waiting or done. A file that is not there reads as empty, so its counts read zero.
/// </summary>
/// <param name="store">The share.</param>
public sealed class FunnelSource(IShareReader store)
{
    /// <summary>Where the 6 AM program leaves its LinkedIn rows, and where the network log moves them once read.</summary>
    public static readonly IReadOnlyList<string> LinkedInFolders = ["networklog/linkedin-inbox", "networklog/linkedin-inbox/done"];

    /// <summary>The day's inputs.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="ct">Cancels the reads.</param>
    public async Task<FunnelInputs> ReadAsync(DateOnly day, CancellationToken ct)
    {
        string d = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var linkedIn = new List<string>();
        foreach (string folder in LinkedInFolders)
        {
            foreach (var file in (await store.ListFilesAsync(folder, ct)).Where(f => f.Name.EndsWith(".tsv", StringComparison.Ordinal)))
            {
                linkedIn.Add(await TextAsync($"{folder}/{file.Name}", ct));
            }
        }
        return new FunnelInputs(
            await TextAsync(MergePaths.Master, ct),
            await TextAsync("campaign/pass.log", ct),
            await TextAsync($"JOBS-{d}.md", ct),
            await TextAsync($"staging/apply-{d}-leaders/OUTREACH-DRAFTS.md", ct),
            linkedIn);
    }

    /// <summary>The funnel's log, empty when no run has written it yet.</summary>
    /// <param name="ct">Cancels the read.</param>
    public Task<string> LogAsync(CancellationToken ct) => TextAsync(FunnelWeek.Log, ct);

    private async Task<string> TextAsync(string path, CancellationToken ct) =>
        await store.ExistsAsync(path, ct) ? TrackerCsvText.Decode(await store.ReadAsync(path, ct)) : string.Empty;
}
