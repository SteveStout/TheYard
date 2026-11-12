namespace JobHunt.Application.Features.Jobs;

/// <summary>What one tick handed to the box.</summary>
/// <param name="Items">Roles whose hand-off and first-touch wrapper were written.</param>
/// <param name="PagesFailed">Page items the share would not take; their roles' first touches stand.</param>
public sealed record HandedOff(int Items, int PagesFailed);

/// <summary>
/// Writes the hand-off and the dispatcher items for each role a tick passed: for every role the JSON first, so no item
/// names a file that is not there, then the wrapper that starts the first touch (PounceHandoff); only once every
/// wrapper is placed, the item that builds each role's 90-day page in shadow (PouncePage). Each item goes under a name
/// the dispatcher does not read and is renamed into place in one step, so none starts from half a file. A page item
/// the share will not take is counted and skipped: it stops no first touch and no other role's page. The mode "off"
/// writes nothing.
/// </summary>
public static class PounceHandoffs
{
    /// <summary>Writes each role's hand-off and items; answers how many wrappers were written and how many pages failed.</summary>
    /// <param name="store">The job-hunt share.</param>
    /// <param name="passed">The roles the tick passed, each with its title and address.</param>
    /// <param name="mode">Off, dry or live (PounceHandoff.Mode).</param>
    /// <param name="ct">Stops the writes.</param>
    public static async Task<HandedOff> WriteAsync(IMergeStore store, IReadOnlyList<(PounceRole Role, string Title, string Url)> passed, string mode, CancellationToken ct)
    {
        if (mode == PounceHandoff.Off || passed.Count == 0)
        {
            return new HandedOff(0, 0);
        }
        await store.EnsureFolderAsync(PounceHandoff.Folder, ct);
        await store.EnsureFolderAsync(PounceHandoff.Ready, ct);
        var caught = passed.Select(p => new CaughtRole(p.Role.Key, p.Role.Company, p.Title, p.Url, p.Role.BandFlag, p.Role.LiveAt, p.Role.LiveKnown, p.Role.BudgetFlag, p.Role.JudgedAt, p.Role.CompanyKey)).ToList();
        foreach (CaughtRole role in caught)
        {
            await store.WriteAsync(PounceHandoff.Path(role), TrackerCsvText.Encode(PounceHandoff.Json(role)), ct);
            await PlaceAsync(store, $"{PounceHandoff.Ready}/.pounce-{role.Key}.tmp", PounceHandoff.Item(role, mode), PounceHandoff.ReadyPath(role), ct);
        }
        int failed = 0;
        foreach (CaughtRole role in caught)
        {
            try
            {
                await PlaceAsync(store, $"{PounceHandoff.Ready}/.pounce90-{role.Key}.tmp", PouncePage.Item(role), PouncePage.ReadyPath(role), ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                failed++;
            }
        }
        return new HandedOff(caught.Count, failed);
    }

    private static async Task PlaceAsync(IMergeStore store, string temp, string text, string path, CancellationToken ct)
    {
        await store.WriteAsync(temp, TrackerCsvText.Encode(text), ct);
        await store.ReplaceAsync(temp, path, ct);
    }
}
