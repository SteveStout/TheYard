using System.Diagnostics;
using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The shared .md files' appends in Azure (TextAppendMerge), the port of merge-tracker.ps1's
/// Merge-TextAppends: the exact bytes a fragment adds, a stamp already present, an empty fragment, a
/// folder that is not a target, a target that is missing, a stamp that vanished, and the cloud lanes'
/// own spool. The parity test runs the PowerShell merger beside it on copies of the real files.
/// </summary>
public sealed class TextAppendMergeTests
{
    private static Task RunAsync(Scratch s) =>
        new TrackerMerger(s.Store, TimeProvider.System).RunPassAsync(CancellationToken.None, appends: true);

    [Fact]
    public async Task A_fragment_is_appended_verbatim_with_its_stamp_and_filed_under_merged()
    {
        using var s = new Scratch();
        s.Write("state.md", "# state\r\nlast line");
        s.Write("append-inbox/state.md/20261022-160000Z-lane-row.md", "## LANE: x LIVE\r\n\r\n\r\n");

        await RunAsync(s);

        Assert.Equal("# state\r\nlast line\r\n\r\n## LANE: x LIVE\r\n<!-- AID:20261022-160000Z-lane-row -->\r\n", s.Text("state.md"));
        Assert.True(s.Has("append-inbox/state.md/merged/20261022-160000Z-lane-row.md"));
        Assert.Contains("APPENDED 20261022-160000Z-lane-row.md -> state.md", s.Text(TextAppendMerge.Log), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Appends_are_off_unless_asked_for()
    {
        using var s = new Scratch();
        s.Write("state.md", "# state\r\n");
        s.Write("append-inbox/state.md/20261022-160000Z-lane-row.md", "row");

        await s.RunAsync();

        Assert.Equal("# state\r\n", s.Text("state.md"));
        Assert.True(s.Has("append-inbox/state.md/20261022-160000Z-lane-row.md"));
    }

    [Fact]
    public async Task A_stamp_already_present_an_empty_fragment_and_the_cloud_spool()
    {
        using var s = new Scratch();
        s.Write("failure-log.md", "# log\r\nold\r\n<!-- AID:20261022-150000Z-lane-dup -->\r\n");
        s.Write("append-inbox/failure-log.md/20261022-150000Z-lane-dup.md", "again");
        s.Write("append-inbox/failure-log.md/20261022-150100Z-lane-blank.md", "  \r\n\r\n");
        s.Write("inbox/append-inbox/failure-log.md/20261022-150200Z-cloud-entry.md", "from the cloud");
        byte[] before = s.Bytes("failure-log.md");

        await RunAsync(s);

        string text = s.Text("failure-log.md");
        Assert.StartsWith(Encoding.UTF8.GetString(before), text, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(text, "AID:20261022-150000Z-lane-dup"));
        Assert.DoesNotContain("AID:20261022-150100Z-lane-blank", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\nfrom the cloud\r\n<!-- AID:20261022-150200Z-cloud-entry -->\r\n", text, StringComparison.Ordinal);
        Assert.True(s.Has("append-inbox/failure-log.md/merged/20261022-150000Z-lane-dup.md"));
        Assert.True(s.Has("append-inbox/failure-log.md/merged/20261022-150100Z-lane-blank.md"));
        Assert.True(s.Has("inbox/append-inbox/failure-log.md/merged/20261022-150200Z-cloud-entry.md"));
    }

    [Fact]
    public async Task A_folder_that_is_not_a_target_and_a_missing_target_are_left_and_said_once()
    {
        using var s = new Scratch();
        s.Write("append-inbox/secrets.md/20261022-150000Z-lane-x.md", "not allowed");
        s.Write("append-inbox/responders.md/20261022-150000Z-lane-y.md", "no responders.md in this folder");
        var merger = new TrackerMerger(s.Store, TimeProvider.System);

        await merger.RunPassAsync(CancellationToken.None, appends: true);
        await merger.RunPassAsync(CancellationToken.None, appends: true);

        Assert.False(s.Has("secrets.md"));
        Assert.True(s.Has("append-inbox/secrets.md/20261022-150000Z-lane-x.md"));
        Assert.True(s.Has("append-inbox/responders.md/20261022-150000Z-lane-y.md"));
        string log = s.Text(TextAppendMerge.Log);
        Assert.Equal(1, CountOf(log, "SKIPPED append-inbox/secrets.md: not an allowed target"));
        Assert.Equal(1, CountOf(log, "SKIPPED append-inbox/responders.md: target file missing"));
    }

    [Fact]
    public async Task A_stamp_that_vanished_from_the_target_is_appended_again()
    {
        using var s = new Scratch();
        s.Write("state.md", "# state\r\n");
        s.Write("append-inbox/state.md/20261022-160000Z-lane-row.md", "row one");
        await RunAsync(s);
        s.Write("state.md", "# state, replaced whole by another writer\r\n");

        await RunAsync(s);

        Assert.Contains("REVERTED: AID:20261022-160000Z-lane-row is gone from state.md; re-queued", s.Text(TextAppendMerge.Log), StringComparison.Ordinal);
        Assert.EndsWith("row one\r\n<!-- AID:20261022-160000Z-lane-row -->\r\n", s.Text("state.md"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fragment_filed_under_a_dup_name_is_not_appended_again_and_a_dot_file_is_not_a_fragment()
    {
        using var s = new Scratch();
        s.Write("standard-answers.md", "# answers\r\nan answer\r\n<!-- AID:20261022-001631Z-chat-storee -->\r\n");
        s.Write("append-inbox/standard-answers.md/merged/20261022-001631Z-chat-storee.md", "an answer");
        s.Write("append-inbox/standard-answers.md/20261022-001631Z-chat-storee.md", "an answer");
        s.Write("append-inbox/standard-answers.md/.azDownload-1234-20261022-001631Z-chat-storee.md", "half a copy");
        byte[] before = s.Bytes("standard-answers.md");

        await RunAsync(s);
        await RunAsync(s);
        await RunAsync(s);

        Assert.Equal(before, s.Bytes("standard-answers.md"));
        Assert.Single(Directory.GetFiles(Path.Combine(s.Root, "append-inbox", "standard-answers.md", "merged"), "*-dup*"));
        Assert.DoesNotContain("REVERTED", s.Text(TextAppendMerge.Log), StringComparison.Ordinal);
        Assert.Equal("x.md", TextAppendMerge.Undup("x-dup115322.md"));
        Assert.Equal("x-dupe.md", TextAppendMerge.Undup("x-dupe.md"));
    }

    [Fact]
    public void The_allowed_targets_are_merge_tracker_ps1_s_list()
    {
        string? script = Environment.GetEnvironmentVariable("MERGE_TRACKER_PS");
        if (string.IsNullOrEmpty(script) || !File.Exists(script))
        {
            Console.WriteLine("allowed targets: not compared (MERGE_TRACKER_PS not set)");
            return;
        }
        string line = File.ReadAllLines(script).Single(l => l.TrimStart().StartsWith("$allowedTargets = @(", StringComparison.Ordinal));
        var listed = line[(line.IndexOf('(') + 1)..line.LastIndexOf(')')].Split(',').Select(p => p.Trim().Trim('\'')).ToArray();
        Assert.Equal(listed, TextAppendMerge.AllowedTargets);
    }

    /// <summary>
    /// The cloud appends against merge-tracker.ps1 -AppendsOnly on copies of the real .md files, with a
    /// fragment that has no final line break, one with only LF line ends and trailing blank lines, one
    /// with a byte order mark and characters past ASCII, one whose stamp is already in its target, an
    /// empty one, and one for a folder that is not a target. Every target's bytes must match. It runs
    /// when MERGE_TRACKER_PS names merge-tracker.ps1 and PARITY_MD_DATA names a folder holding copies of
    /// the targets; elsewhere it reports that it did not run.
    /// </summary>
    [Fact]
    public async Task The_cloud_appends_leave_the_same_bytes_as_the_laptop_merger()
    {
        string? script = Environment.GetEnvironmentVariable("MERGE_TRACKER_PS");
        string? data = Environment.GetEnvironmentVariable("PARITY_MD_DATA");
        if (string.IsNullOrEmpty(script) || string.IsNullOrEmpty(data) || !OperatingSystem.IsWindows())
        {
            Console.WriteLine("append parity: not run (MERGE_TRACKER_PS or PARITY_MD_DATA not set, or not Windows)");
            return;
        }
        using var laptop = new Scratch();
        using var cloud = new Scratch();
        var targets = TextAppendMerge.AllowedTargets.Where(t => File.Exists(Path.Combine(data, t))).ToList();
        Assert.Contains("state.md", targets);
        foreach (Scratch s in new[] { laptop, cloud }) { SeedAppends(s, data, targets); }

        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Root \"{laptop.Root}\" -AppendsOnly")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using (Process ps = Process.Start(psi)!)
        {
            Console.WriteLine(await ps.StandardOutput.ReadToEndAsync());
            await ps.WaitForExitAsync();
        }
        await RunAsync(cloud);

        foreach (string target in targets)
        {
            Assert.True(laptop.Bytes(target).AsSpan().SequenceEqual(cloud.Bytes(target)), $"{target}: laptop {laptop.Bytes(target).Length} B, cloud {cloud.Bytes(target).Length} B");
            foreach (string folder in new[] { $"append-inbox/{target}", $"append-inbox/{target}/merged" })
            {
                Assert.Equal(Names(laptop, folder), Names(cloud, folder));
            }
        }
        Assert.True(cloud.Has("append-inbox/not-a-target.md/20261022-160500Z-parity-x.md"));
        Console.WriteLine("append parity: " + string.Join(", ", targets.Select(t => $"{t} {cloud.Bytes(t).Length} B")) + " identical");
    }

    private static void SeedAppends(Scratch s, string data, IReadOnlyList<string> targets)
    {
        foreach (string target in targets)
        {
            File.Copy(Path.Combine(data, target), Path.Combine(s.Root, target));
        }
        string first = targets.Contains("failure-log.md") ? "failure-log.md" : "state.md";
        File.AppendAllText(Path.Combine(s.Root, first), "\r\n<!-- AID:20261022-160300Z-parity-present -->\r\n", new UTF8Encoding(false));
        s.Write("append-inbox/state.md/20261022-160000Z-parity-plain.md", "## LANE: parity LIVE, no final line break");
        s.Write("append-inbox/state.md/20261022-160100Z-parity-lf.md", "line one\nline two\n\n\n");
        s.Write("append-inbox/state.md/20261022-160200Z-parity-bom.md", "café – naïve, “quoted”", bom: true);
        s.Write($"append-inbox/{first}/20261022-160300Z-parity-present.md", "already in the target");
        s.Write("append-inbox/state.md/20261022-160400Z-parity-empty.md", " \r\n");
        s.Write("append-inbox/not-a-target.md/20261022-160500Z-parity-x.md", "left where it is");
    }

    private static string[] Names(Scratch s, string folder)
    {
        string full = Path.Combine(s.Root, folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(full)
            ? Directory.GetFiles(full).Select(Path.GetFileName).Select(n => n!).Where(n => !n.EndsWith(".log", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : [];
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) { count++; }
        return count;
    }
}
