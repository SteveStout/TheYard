namespace JobHunt.Application.Features.Jobs;

/// <summary>
/// Writes the hand-off and the dispatcher items for each role a tick passed: the JSON first, so no item names a file
/// that is not there, then the wrapper that starts the first touch (PounceHandoff), then the item that builds the role's
/// 90-day page in shadow (PouncePage), each under a name the dispatcher does not read and renamed into place in one step,
/// so neither starts from half a file. The mode "off" writes nothing.
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
            var caught = new CaughtRole(role.Key, role.Company, title, url, role.BandFlag, role.LiveAt, role.LiveKnown, role.BudgetFlag, role.JudgedAt, role.CompanyKey);
            await store.WriteAsync(PounceHandoff.Path(caught), TrackerCsvText.Encode(PounceHandoff.Json(caught)), ct);
            await PlaceAsync(store, $"{PounceHandoff.Ready}/.pounce-{role.Key}.tmp", PounceHandoff.Item(caught, mode), PounceHandoff.ReadyPath(caught), ct);
            await PlaceAsync(store, $"{PounceHandoff.Ready}/.pounce90-{role.Key}.tmp", PouncePage.Item(caught), PouncePage.ReadyPath(caught), ct);
        }
        return passed.Count;
    }

    private static async Task PlaceAsync(IMergeStore store, string temp, string text, string path, CancellationToken ct)
    {
        await store.WriteAsync(temp, TrackerCsvText.Encode(text), ct);
        await store.ReplaceAsync(temp, path, ct);
    }
}
