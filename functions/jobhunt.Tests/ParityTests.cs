using System.Diagnostics;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The cloud merger against the laptop's PowerShell merger on the same inputs: a copy of the real
/// master and moved-out file, rows with commas, quotes and line breaks, a header-only file, a fix
/// and an approved moveout. Both must leave byte-identical files and file the same spools. It runs
/// when MERGE_TRACKER_PS names merge-tracker.ps1 and PARITY_DATA names a folder holding a copy of
/// Job_Hunt_Tracker.csv and Job_Hunt_Tracker_MOVED_OUT.csv; elsewhere it reports that it did not run.
/// </summary>
public sealed class ParityTests
{
    [Fact]
    public async Task The_cloud_merger_leaves_the_same_bytes_as_the_laptop_merger()
    {
        string? script = Environment.GetEnvironmentVariable("MERGE_TRACKER_PS");
        string? data = Environment.GetEnvironmentVariable("PARITY_DATA");
        if (string.IsNullOrEmpty(script) || string.IsNullOrEmpty(data) || !OperatingSystem.IsWindows())
        {
            Console.WriteLine("parity: not run (MERGE_TRACKER_PS or PARITY_DATA not set, or not Windows)");
            return;
        }
        using var laptop = new Scratch();
        using var cloud = new Scratch();
        foreach (Scratch s in new[] { laptop, cloud }) { Seed(s, data); }

        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Root \"{laptop.Root}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (Process ps = Process.Start(psi)!)
        {
            string output = await ps.StandardOutput.ReadToEndAsync();
            await ps.WaitForExitAsync();
            Console.WriteLine(output);
        }
        await cloud.RunAsync();

        Assert.Equal(laptop.Bytes(MergePaths.Master), cloud.Bytes(MergePaths.Master));
        Assert.Equal(laptop.Bytes(MergePaths.MovedOut), cloud.Bytes(MergePaths.MovedOut));
        foreach (string folder in new[] { "tracker-inbox", "tracker-inbox/merged", "tracker-inbox/rejected" })
        {
            Assert.Equal(Names(laptop, folder), Names(cloud, folder));
        }
        Console.WriteLine($"parity: master {cloud.Bytes(MergePaths.Master).Length} B and moved-out {cloud.Bytes(MergePaths.MovedOut).Length} B identical");
    }

    private static void Seed(Scratch s, string data)
    {
        File.Copy(Path.Combine(data, MergePaths.Master), Path.Combine(s.Root, MergePaths.Master));
        File.Copy(Path.Combine(data, MergePaths.MovedOut), Path.Combine(s.Root, MergePaths.MovedOut));
        string master = s.Text(MergePaths.Master);
        string movedOut = s.Text(MergePaths.MovedOut);
        List<string[]> rows = TrackerCsvText.Parse(master);
        int next = TrackerCsvText.MaxCount(rows.Concat(TrackerCsvText.Parse(movedOut))) + 1;
        string[] last = rows[^1];

        s.Spool("20261022-150000Z-parity-a.csv", "Person,Company,Communication,Date,Phone/Email,Notes\r\nParity A,\"Comma, Inc\",Email,10/22/2026,a@x.com,\"quote \"\"q\"\" and\r\na new line\"\r\n");
        s.Spool("20261022-150100Z-parity-b.csv", "999,Parity B,Beta,LinkedIn,10/22/2026,,seven columns\r\nParity C,Gamma,Phone,10/22/2026,,\r\n");
        s.Spool("20261022-150200Z-parity-empty.csv", "Person,Company,Communication,Date,Phone/Email,Notes\r\n");
        string fixedNotes = last[6] + " parity fix";
        s.Spool($"fix-{last[0]}-parity-fix.csv", TrackerCsvText.Line(new[] { last[0], last[1], last[2], last[3], last[4], last[5], fixedNotes }) + "\r\n");
        s.Spool("moveout-parity-move.txt", $"# approved-by: Steve parity test\r\n{next}\r\n");
    }

    private static string[] Names(Scratch s, string folder)
    {
        string full = Path.Combine(s.Root, folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(full)
            ? Directory.GetFiles(full).Select(Path.GetFileName).Where(n => n is not null && !n.EndsWith(".log") && !n.StartsWith(".merge")).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : [];
    }
}
