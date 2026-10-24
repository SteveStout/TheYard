namespace JobHunt.Application.Features.Merge;

/// <summary>
/// The shared .md files' appends, the port of merge-tracker.ps1's Merge-TextAppends. A lane drops a
/// fragment into append-inbox/&lt;target&gt;/ (the laptop's, carried up by the sync) or
/// inbox/append-inbox/&lt;target&gt;/ (a cloud lane's, on the share), named
/// &lt;yyyyMMdd-HHmmss&gt;Z-&lt;lane&gt;-&lt;slug&gt;.md. The fragment is appended verbatim to the end of the
/// target, stamped &lt;!-- AID:&lt;file stem&gt; --&gt;, read back, and filed under merged/. Only the targets on
/// the allowed list are written. A fragment whose stamp is already in the target is filed without a
/// second append; a merged fragment younger than 24 hours whose stamp has vanished from the target
/// (something replaced the whole file) is re-queued and appended again, logged as REVERTED. Nothing is
/// deleted.
/// </summary>
/// <param name="context">The pass's store, clock and log.</param>
/// <param name="said">The skips already logged, kept by the merger across passes so each is said once a process.</param>
public sealed class TextAppendMerge(MergeContext context, ISet<string> said)
{
    /// <summary>The two spool roots: the laptop lanes' (carried up) and the cloud lanes'.</summary>
    public static readonly IReadOnlyList<string> Roots = ["append-inbox", "inbox/append-inbox"];

    /// <summary>The append log, beside the laptop's own name for it.</summary>
    public const string Log = "append-inbox/merge.log";

    /// <summary>The files a fragment may be appended to: merge-tracker.ps1's $allowedTargets, the same list.</summary>
    public static readonly IReadOnlyList<string> AllowedTargets =
        ["state.md", "writing-samples.md", "responders.md", "failure-log.md", "open-questions.md", "WORKAROUNDS-LEDGER.md", "claude-confidence.md", "CONVENTIONS.md", "standard-answers.md"];

    public async Task RunAsync(string root, CancellationToken ct)
    {
        foreach (string target in (await context.Store.ListFoldersAsync(root, ct)).OrderBy(name => name, StringComparer.Ordinal))
        {
            string folder = MergePaths.In(root, target);
            if (!AllowedTargets.Contains(target, StringComparer.Ordinal))
            {
                if (said.Add("target:" + folder)) { await LogAsync($"SKIPPED {folder}: not an allowed target ({string.Join(", ", AllowedTargets)})", ct); }
                continue;
            }
            if (!await context.Store.ExistsAsync(target, ct))
            {
                if (said.Add("missing:" + folder)) { await LogAsync($"SKIPPED {folder}: target file missing", ct); }
                continue;
            }
            string merged = MergePaths.Merged(folder);
            await context.Store.EnsureFolderAsync(merged, ct);
            await RequeueVanishedAsync(folder, target, merged, ct);
            foreach (MergeEntry file in (await context.Store.ListFilesAsync(folder, ct))
                         .Where(f => IsFragment(f.Name))
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                await AppendAsync(folder, target, merged, file.Name, ct);
            }
        }
    }

    private static string Aid(string name) => $"<!-- AID:{Path.GetFileNameWithoutExtension(name)} -->";

    /// <summary>A merged file's name without the -dupHHMMSS that MoveSafeAsync adds when the name is taken.</summary>
    public static string Undup(string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        int at = stem.LastIndexOf("-dup", StringComparison.Ordinal);
        return at > 0 && stem.Length == at + 10 && stem[(at + 4)..].All(char.IsAsciiDigit) ? stem[..at] + Path.GetExtension(name) : name;
    }

    /// <summary>A lane's fragment: a .md file, never a dot file (azcopy writes .azDownload-* while it copies).</summary>
    private static bool IsFragment(string name) => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !name.StartsWith('.');

    private async Task RequeueVanishedAsync(string folder, string target, string merged, CancellationToken ct)
    {
        var recent = (await context.Store.ListFilesAsync(merged, ct))
            .Where(f => IsFragment(f.Name) && f.LastWritten >= context.UtcNow.AddHours(-24))
            .ToList();
        if (recent.Count == 0) { return; }
        string text = await context.ReadTextAsync(target, ct);
        foreach (MergeEntry file in recent)
        {
            // A fragment filed as already present under a name merged/ already held was renamed -dupHHMMSS on the
            // way in; its stamp is the original name's, so that stamp counts too. Without this the -dup name read
            // as vanished and was appended again (the laptop merger did that 332 times on 2026-10-22).
            if (text.Contains(Aid(file.Name), StringComparison.Ordinal) || text.Contains(Aid(Undup(file.Name)), StringComparison.Ordinal)) { continue; }
            await LogAsync($"REVERTED: AID:{Path.GetFileNameWithoutExtension(file.Name)} is gone from {target}; re-queued", ct);
            await context.Store.MoveAsync(MergePaths.In(merged, file.Name), MergePaths.In(folder, file.Name), ct);
        }
    }

    private async Task AppendAsync(string folder, string target, string merged, string name, CancellationToken ct)
    {
        string aid = Aid(name);
        string text = await context.ReadTextAsync(target, ct);
        if (text.Contains(aid, StringComparison.Ordinal))
        {
            await LogAsync($"already present {aid} in {target}, moved to merged", ct);
            await context.MoveSafeAsync(folder, name, merged, ct);
            return;
        }
        string fragment = (await context.ReadTextAsync(MergePaths.In(folder, name), ct)).TrimEnd();
        if (fragment.Length == 0)
        {
            await LogAsync($"REJECTED empty fragment {name}", ct);
            await context.MoveSafeAsync(folder, name, merged, ct);
            return;
        }
        string chunk = (text.EndsWith('\n') ? "" : "\r\n") + "\r\n" + fragment + "\r\n" + aid + "\r\n";
        await context.Store.AppendAsync(target, TrackerCsvText.Encode(chunk), ct);
        byte[] after = await context.ReadBytesAsync(target, ct);
        if (!TrackerCsvText.Decode(after).Contains(aid, StringComparison.Ordinal))
        {
            await LogAsync($"APPENDED BUT NOT FOUND ON RE-READ: {name} -> {target}; left for the next pass", ct);
            return;
        }
        await LogAsync($"APPENDED {name} -> {target} ({fragment.Length} chars); {target} now {after.Length} B", ct);
        await context.MoveSafeAsync(folder, name, merged, ct);
    }

    private async Task LogAsync(string message, CancellationToken ct)
    {
        string line = $"{context.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n";
        await context.Store.AppendAsync(Log, TrackerCsvText.Encode(line), ct);
    }
}
