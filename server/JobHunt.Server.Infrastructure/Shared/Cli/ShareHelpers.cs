using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Infrastructure.Shared.Cli;

/// <summary>
/// The Python helpers on the share, make_ref.py, check_aiisms.py and build_tailored_resume.py, run as every other lane runs them. Their
/// answers are read from their output and their exit code, and neither is reimplemented here, so a change to the
/// rules they hold reaches this process with no build.
/// </summary>
/// <param name="python">The Python to run under.</param>
/// <param name="share">The share.</param>
public sealed partial class ShareHelpers(string python, IShare share) : ICodeMinter, IProseCheck, IResumeBuilder
{
    /// <summary>How long a resume build may run.</summary>
    public TimeSpan BuildLimit { get; init; } = TimeSpan.FromMinutes(3);

    /// <inheritdoc/>
    public async Task<string?> MintAsync(string person, string company, CancellationToken ct)
    {
        (int exit, string output) = await RunAsync(["--person", person, "--company", company, "--channel", "LinkedIn", "--lane", RegistryRows.Lane], null, "make_ref.py", ct);
        return exit == 0 && Minted().Match(output) is { Success: true } match ? match.Groups[1].Value : null;
    }

    /// <inheritdoc/>
    public async Task<ResumeBuild> BuildAsync(string title, string company, string folder, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(BuildLimit);
        int exit;
        string output;
        try
        {
            (exit, output) = await RunAsync([title, "--company", company, "--out", folder], null, "build_tailored_resume.py", limit.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResumeBuild(string.Empty, string.Empty, string.Empty, $"the build timed out at {BuildLimit.TotalMinutes:0} minutes");
        }
        Match path = SendCopy().Match(output), headline = Headline().Match(output), sha = Sha().Match(output);
        if (exit != 0 || !path.Success || !headline.Success || !sha.Success)
        {
            string why = output.Split('\n').FirstOrDefault(line => line.StartsWith("STOP", StringComparison.Ordinal)) ?? $"exit {exit}";
            return new ResumeBuild(string.Empty, string.Empty, string.Empty, why);
        }
        string used = headline.Groups[1].Value.Trim();
        return used.Contains(' ', StringComparison.Ordinal)
            ? new ResumeBuild(path.Groups[1].Value.Trim(), used, sha.Groups[1].Value.Trim())
            : new ResumeBuild(string.Empty, string.Empty, string.Empty, $"the headline came out one word, {used}, from {title}");
    }

    /// <inheritdoc/>
    public async Task<bool> CleanAsync(string text, CancellationToken ct)
    {
        (int exit, _) = await RunAsync(["-"], text, "check_aiisms.py", ct);
        return exit == 0;
    }

    private async Task<(int Exit, string Output)> RunAsync(string[] arguments, string? input, string script, CancellationToken ct)
    {
        var start = new ProcessStartInfo(python, [share.Full(script), .. arguments])
        {
            WorkingDirectory = share.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new IOException($"{script} did not start");
        await SendAsync(process, input, ct);
        _ = process.StandardError.ReadToEndAsync(ct);
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task SendAsync(Process process, string? input, CancellationToken ct)
    {
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), ct);
            }
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // A helper that exits before reading its input answers by its exit code, which is read below.
        }
    }

    [GeneratedRegex(@"^send copy\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SendCopy();

    [GeneratedRegex(@"^headline\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex Headline();

    [GeneratedRegex(@"^sha256\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex Sha();

    [GeneratedRegex(@"^code ([a-z0-9]{6}) for ", RegexOptions.Multiline)]
    private static partial Regex Minted();
}
