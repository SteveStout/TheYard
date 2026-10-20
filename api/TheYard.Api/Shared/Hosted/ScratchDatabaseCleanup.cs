// The scratch SQLite database a process gets when no database is configured, said out loud at
// startup and deleted when the process has stopped.
using TheYard.Infrastructure;

namespace TheYard.Api;

/// <summary>
/// A scratch database belongs to one process, so it goes when the process does. Where a
/// database was configured, this does nothing.
/// </summary>
public sealed class ScratchDatabaseCleanup(ScratchDatabase scratch, IHostApplicationLifetime lifetime, ILogger<ScratchDatabaseCleanup> logger) : IHostedService
{
    /// <summary>Says the scratch database is in use and arranges its deletion once the host has stopped.</summary>
    /// <param name="cancellationToken">The host's start cancellation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (scratch.ScratchPath is not { } path)
        {
            return Task.CompletedTask;
        }

        logger.LogWarning(
            "No ConnectionStrings:Yard is configured, so this process is using a scratch database in the system temp folder and will delete it on shutdown");
        // The path itself at Debug, which the Admin tab's log section does not capture: a temp
        // path names the account the process runs as, and that page is public.
        logger.LogDebug("The scratch database is at {Path}", path);
        // After the host has stopped, when nothing holds a connection any more. The pool is
        // emptied first or the handle is still open and the delete fails on Windows.
        lifetime.ApplicationStopped.Register(() => Delete(path));
        return Task.CompletedTask;
    }

    /// <summary>Nothing to stop; the delete runs once the host has stopped.</summary>
    /// <param name="cancellationToken">The host's stop cancellation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void Delete(string path)
    {
        // This database's pooled connections closed first, so its file is not held.
        YardDatabase.ReleaseFile(path);
        // SQLite writes up to four files: the database, the write-ahead log, the shared-memory
        // index and the journal. This runs whether or not the store came up, because a
        // half-created file is the case most likely to be left behind.
        foreach (string leftover in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover scratch file is litter, not a reason to fail a shutdown.
            }
        }
    }
}
