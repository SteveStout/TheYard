using System.Diagnostics;

namespace JobHunt.Server.Infrastructure.Shared.Cli;

/// <summary>
/// Chrome on the auto-login desktop. The extension answers only while Chrome runs, and nothing starts Chrome at boot,
/// so a morning can find it down. It is started in a transient unit of its own: a child of this oneshot service would
/// die with the service at the end of the run. It keeps the profile the extension and the LinkedIn session live in.
/// </summary>
public sealed class ChromeDesktop : IDesktop
{
    private const string Unit = "jobhunt-chrome";

    /// <inheritdoc/>
    public async Task<string?> EnsureChromeAsync(CancellationToken ct)
    {
        if (await RunningAsync(ct))
        {
            return null;
        }
        string[] arguments = ["--user", "--collect", $"--unit={Unit}", "--setenv=DISPLAY=:0", $"--setenv=XAUTHORITY={Environment.GetEnvironmentVariable("HOME")}/.Xauthority", "/usr/bin/google-chrome", "--no-first-run"];
        using Process? start = Process.Start(new ProcessStartInfo("systemd-run", arguments) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
        if (start is not null)
        {
            await start.WaitForExitAsync(ct);
        }
        for (int wait = 0; wait < 6; wait++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            if (await RunningAsync(ct))
            {
                return null;
            }
        }
        return "Chrome would not start on the desktop session";
    }

    private static async Task<bool> RunningAsync(CancellationToken ct)
    {
        using Process? check = Process.Start(new ProcessStartInfo("pgrep", ["-x", "chrome"]) { UseShellExecute = false, RedirectStandardOutput = true });
        if (check is null)
        {
            return false;
        }
        await check.WaitForExitAsync(ct);
        return check.ExitCode == 0;
    }
}
