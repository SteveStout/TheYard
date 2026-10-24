using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Extensions.Logging;

namespace JobHunt.Infrastructure;

/// <summary>
/// One snapshot of the share a night, fourteen kept. Done here rather than by Azure Backup,
/// which would add a vault this lane does not need.
/// </summary>
public sealed class ShareSnapshot(ShareClient share, ILogger<ShareSnapshot> log)
{
    public const int Keep = 14;

    public async Task<string> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var made = await share.CreateSnapshotAsync(cancellationToken: ct);
        var service = new ShareServiceClient(Environment.GetEnvironmentVariable("JOBHUNT_SHARE_CONNECTION"));
        int pruned = 0;
        await foreach (ShareItem item in service.GetSharesAsync(ShareTraits.None, ShareStates.Snapshots, share.Name, ct))
        {
            if (item.Name == share.Name && item.Snapshot is { } stamp
                && DateTimeOffset.TryParse(stamp, out var at) && at < now.AddDays(-Keep))
            {
                await share.WithSnapshot(stamp).DeleteIfExistsAsync(cancellationToken: ct);
                pruned++;
            }
        }
        string line = $"snapshot {made.Value.Snapshot}, pruned {pruned} older than {Keep} days";
        log.LogInformation("{Line}", line);
        return line;
    }
}
