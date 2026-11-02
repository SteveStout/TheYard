using System.Diagnostics;
using System.Text;

namespace JobHunt.Server.Infrastructure.Shared.Cli;

/// <summary>
/// Starts claude -p, keeps the stream, and kills the process tree at the wall-clock limit. The stream goes to a local
/// file first and to the share at the end, because a line at a time over the mount is slow, and a line holding a
/// screenshot is cut so a run folder stays readable. The result line is taken from the stream as it passes. A stop
/// from the service, like the limit, kills the tree and hands back what was seen, so the run can still write its records.
/// </summary>
/// <param name="path">The CLI's path.</param>
public sealed class ClaudeCli(string path) : IClaudeCli
{
    private const int LongestLine = 64 * 1024;
    private const string ResultMark = "\"type\":\"result\"";

    private static void Keep(string local, string target)
    {
        try
        {
            File.Copy(local, target, overwrite: true);
            File.Delete(local);
        }
        catch (IOException)
        {
            // The stream is a convenience; a share that refuses it must not cost the run's records, so it stays local.
        }
    }

    /// <inheritdoc/>
    public async Task<ClaudeRun> RunAsync(ClaudeRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(request.WorkingFolder);
        var start = new ProcessStartInfo(path, request.Arguments)
        {
            WorkingDirectory = request.WorkingFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new IOException($"{path} did not start");
        process.StandardInput.Close();
        _ = process.StandardError.ReadToEndAsync(ct);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(request.Timeout);
        string local = Path.GetTempFileName();
        string? result = null;
        bool timedOut = false;
        await using (var kept = new StreamWriter(local, append: false, new UTF8Encoding(false)))
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync(limit.Token) is { } line)
                {
                    await kept.WriteLineAsync(line.Length > LongestLine ? line[..1024] + " [cut]" : line);
                    if (line.Contains(ResultMark, StringComparison.Ordinal))
                    {
                        result = line;
                    }
                }
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        Keep(local, request.StreamFile);
        return new ClaudeRun(process.ExitCode, timedOut, result);
    }
}
