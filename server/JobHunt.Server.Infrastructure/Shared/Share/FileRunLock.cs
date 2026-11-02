using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace JobHunt.Server.Infrastructure.Shared.Share;

/// <summary>
/// One run at a time: a lock file in the day's folder holding the process id and the start. A lock younger than Rule 19's
/// four hours turns the new run away while its process lives. A lock whose process is gone is a crash, and it is replaced
/// so the next run can read what the crash left and say so, rather than do nothing in silence. An older one means a run
/// past the ceiling, which is killed and replaced, and only when the process id still names this program.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="time">The clock.</param>
public sealed class FileRunLock(IShare share, TimeProvider time) : IRunLock
{
    private const string Name = "0600.lock";
    private const string ProcessName = "JobHunt.Server";

    private static readonly TimeSpan Ceiling = TimeSpan.FromHours(4);

    /// <inheritdoc/>
    public async Task<IAsyncDisposable?> TakeAsync(string dayFolder, CancellationToken ct)
    {
        string path = $"{dayFolder}/{Name}";
        string stamp = $"{Environment.ProcessId}\t{time.GetUtcNow().ToUnixTimeSeconds()}\n";
        if (await share.ExistsAsync(path, ct))
        {
            if (!await ReplaceStaleAsync(path, ct))
            {
                return null;
            }
            await share.WriteAsync(path, stamp, ct);
        }
        else
        {
            await share.WriteNewAsync(path, stamp, ct);
        }
        return new Held(share.Full(path));
    }

    private async Task<bool> ReplaceStaleAsync(string path, CancellationToken ct)
    {
        string[] cells = (await share.ReadAsync(path, ct)).Trim().Split('\t');
        if (cells.Length != 2 || !long.TryParse(cells[1], CultureInfo.InvariantCulture, out long since))
        {
            return false;
        }
        if (!int.TryParse(cells[0], CultureInfo.InvariantCulture, out int pid) || !Running(pid))
        {
            return true;
        }
        if (time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(since) < Ceiling)
        {
            return false;
        }
        Stop(pid);
        return true;
    }

    private static bool Running(int pid)
    {
        try
        {
            using Process old = Process.GetProcessById(pid);
            return pid == Environment.ProcessId || old.ProcessName.StartsWith(ProcessName, StringComparison.Ordinal);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static void Stop(int pid)
    {
        try
        {
            using Process old = Process.GetProcessById(pid);
            if (old.ProcessName.StartsWith(ProcessName, StringComparison.Ordinal))
            {
                old.Kill(entireProcessTree: true);
            }
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The old run has gone, or is not this program's: a reused process id is never killed.
        }
    }

    private sealed class Held(string full) : IAsyncDisposable
    {
        /// <summary>Frees the lock.</summary>
        public ValueTask DisposeAsync()
        {
            try
            {
                File.Delete(full);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A share that will not free the lock must not cost the close: the next run reads this lock as a crash.
            }
            return ValueTask.CompletedTask;
        }
    }
}
