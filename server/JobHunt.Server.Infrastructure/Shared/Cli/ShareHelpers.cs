using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Infrastructure.Shared.Cli;

/// <summary>
/// The two Python helpers on the share, make_ref.py and check_aiisms.py, run as every other lane runs them. Their
/// answers are read from their output and their exit code, and neither is reimplemented here, so a change to the
/// rules they hold reaches this process with no build.
/// </summary>
/// <param name="python">The Python to run under.</param>
/// <param name="share">The share.</param>
public sealed partial class ShareHelpers(string python, IShare share) : ICodeMinter, IProseCheck
{
    /// <inheritdoc/>
    public async Task<string?> MintAsync(string person, string company, CancellationToken ct)
    {
        (int exit, string output) = await RunAsync(["--person", person, "--company", company, "--channel", "LinkedIn", "--lane", RegistryRows.Lane], null, "make_ref.py", ct);
        return exit == 0 && Minted().Match(output) is { Success: true } match ? match.Groups[1].Value : null;
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
        string output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, output);
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

    [GeneratedRegex(@"^code ([a-z0-9]{6}) for ", RegexOptions.Multiline)]
    private static partial Regex Minted();
}
