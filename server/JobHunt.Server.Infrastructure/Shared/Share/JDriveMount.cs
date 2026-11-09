using System.Diagnostics;

namespace JobHunt.Server.Infrastructure.Shared.Share;

/// <summary>
/// The mount's freshness: a read of systemRules.md must finish inside ten seconds. A slow or failed read restarts the
/// jdrive unit once and reads again; a second failure is the run's stop, in one line.
/// </summary>
/// <param name="share">The share.</param>
public sealed class JDriveMount(IShare share) : IMount
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public async Task<string?> FreshAsync(CancellationToken ct)
    {
        if (await ReadsFastAsync(ct))
        {
            return null;
        }
        using Process? restart = Process.Start(new ProcessStartInfo("systemctl", ["--user", "restart", "jdrive"]) { UseShellExecute = false });
        if (restart is not null)
        {
            await restart.WaitForExitAsync(ct);
        }
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        return await ReadsFastAsync(ct) ? null : $"the share at {share.Root} would not read systemRules.md within {Limit.TotalSeconds:0} seconds, even after restarting jdrive";
    }

    private async Task<bool> ReadsFastAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        try
        {
            string rules = await share.ReadAsync("systemRules.md", limit.Token);
            return rules.Length > 0;
        }
        catch (Exception failure) when (failure is IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
