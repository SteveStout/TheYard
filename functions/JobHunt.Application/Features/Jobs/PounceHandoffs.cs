namespace JobHunt.Application.Features.Jobs;

/// <summary>
/// Writes the hand-off and the dispatcher item for each role a tick passed (PounceHandoff): the JSON first, so the item
/// never names a file that is not there, then the item under a name the dispatcher does not read and renamed into
/// place in one step, so it never starts from half a file. The mode "off" writes nothing.
/// </summary>
public static class PounceHandoffs
{
    /// <summary>Writes each role's hand-off and item; answers how many items were written.</summary>
    /// <param name="store">The job-hunt share.</param>
    /// <param name="passed">The roles the tick passed, each with its title and address.</param>
    /// <param name="mode">Off, dry or live (PounceHandoff.Mode).</param>
    /// <param name="ct">Stops the writes.</param>
    public static async Task<int> WriteAsync(IMergeStore store, IReadOnlyList<(PounceRole Role, string Title, string Url)> passed, string mode, CancellationToken ct)
    {
        if (mode == PounceHandoff.Off || passed.Count == 0)
        {
            return 0;
        }
        await store.EnsureFolderAsync(PounceHandoff.Folder, ct);
        await store.EnsureFolderAsync(PounceHandoff.Ready, ct);
        foreach (var (role, title, url) in passed)
        {
            var caught = new CaughtRole(role.Key, role.Company, title, url, role.BandFlag, role.LiveAt, role.LiveKnown, role.BudgetFlag, role.JudgedAt);
            await store.WriteAsync(PounceHandoff.Path(caught), TrackerCsvText.Encode(PounceHandoff.Json(caught)), ct);
            string temp = $"{PounceHandoff.Ready}/.pounce-{role.Key}.tmp";
            await store.WriteAsync(temp, TrackerCsvText.Encode(PounceHandoff.Item(caught, mode)), ct);
            await store.ReplaceAsync(temp, PounceHandoff.ReadyPath(caught), ct);
        }
        return passed.Count;
    }
}
