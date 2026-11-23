using System.IO.Compression;
using JobHunt.Server.Infrastructure.Shared.Share;

namespace JobHunt.Server.Tests;

/// <summary>
/// The archive job's zip, verify and move on a temp folder standing in for the share: a day folder zipped with its
/// paths kept and reopened to the same lengths and CRC-32s, the whole job end to end, a second zip of the same name
/// taking -2, a zip set aside as .failed, a source outside the job's five folders or a target outside its two refused.
/// Nothing here reaches the share.
/// </summary>
public sealed class ArchiveAdapterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 40, 0, TimeSpan.Zero);

    private readonly string root = Directory.CreateTempSubdirectory("archive-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private void Put(string relative, string text)
    {
        string full = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    [Fact]
    public async Task A_day_folder_zips_with_its_paths_kept_and_reopens_to_the_same_lengths_and_crcs()
    {
        Put("nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md", "# done\n");
        Put("nightly/2026-10-20/0400-reply-sonnet-5-5/drafts/a.md", new string('x', 70_000));
        var share = new ShareFolder(root);
        var archiver = new MountArchiver(share);
        var unit = ArchiveUnits.DayUnit(ArchiveUnits.Nightly, new DateOnly(2026, 10, 20));

        ArchiveZip zip = await archiver.ZipAsync(unit, ArchiveUnits.ZipPath(unit), CancellationToken.None);
        var inZip = await archiver.OpenAsync(zip.Zip, CancellationToken.None);
        var source = await archiver.ReadAsync(unit, CancellationToken.None);

        Assert.Equal("_archive/datamove1123/nightly/2026-10-20.zip", zip.Zip);
        Assert.Equal(64, zip.Sha256.Length);
        Assert.Equal(new FileInfo(Path.Combine(root, zip.Zip)).Length, zip.Bytes);
        Assert.Equal(source, inZip);
        Assert.Equal(["nightly/2026-10-20/0400-reply-sonnet-5-5/drafts/a.md", "nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md"], source.Select(e => e.Path));
        Put("dispatch/done/2026-10-21/check.txt", "123456789");
        Assert.Equal(0xCBF43926u, Assert.Single(await archiver.ReadAsync(ArchiveUnits.DayUnit(ArchiveUnits.Done, new DateOnly(2026, 10, 21)), CancellationToken.None)).Crc);
        using (var opened = ZipFile.OpenRead(Path.Combine(root, zip.Zip)))
        {
            Assert.Equal(source.Select(e => e.Crc), opened.Entries.Select(e => e.Crc32));
        }
        Assert.Equal("_archive/datamove1123/nightly/2026-10-20-2.zip", (await archiver.ZipAsync(unit, ArchiveUnits.ZipPath(unit), CancellationToken.None)).Zip);
        Assert.Equal("_archive/datamove1123/nightly/2026-10-20.zip.failed", await archiver.FailAsync(zip.Zip, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(root, "_archive/datamove1123/nightly/2026-10-20.zip.failed")));
        Assert.True(archiver.Calls > 0);
    }

    [Fact]
    public async Task The_job_end_to_end_zips_verifies_logs_and_moves_by_one_rename_and_deletes_nothing()
    {
        Put("state.md", "## LANE: other LIVE | IN PROGRESS |\n<!-- AID:20261123-080000Z-other-registry -->\n");
        Put("nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md", "# done\n");
        Put("nightly/2026-10-20/0600-invites-sonnet-5-5/stream.jsonl", "{}\n");
        Put("dispatch/locks/chrome.dead-1792000000", "x");
        Put("dispatch/locks/theyard-gate.lock", "held");
        var share = new ShareFolder(root);

        var (exit, output) = await new ArchiveRun(share, new MountArchiver(share), new FixedTime(Now)).ExecuteAsync(new ArchiveOptions(false, null), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("2 archived, 0 failed, 0 refused", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "nightly/2026-10-20")));
        Assert.True(File.Exists(Path.Combine(root, "_to_delete/datamove1123/nightly/2026-10-20/0600-invites-sonnet-5-5/stream.jsonl")));
        Assert.True(File.Exists(Path.Combine(root, "_to_delete/datamove1123/dispatch-locks/chrome.dead-1792000000")));
        Assert.True(File.Exists(Path.Combine(root, "dispatch/locks/theyard-gate.lock")));
        using (var opened = ZipFile.OpenRead(Path.Combine(root, "_archive/datamove1123/dispatch-locks/2026-10.zip")))
        {
            Assert.Equal(["dispatch/locks/chrome.dead-1792000000"], opened.Entries.Select(e => e.FullName));
        }
        string[] log = File.ReadAllLines(Path.Combine(root, "_archive/datamove1123/ARCHIVE-LOG.tsv"));
        Assert.Equal(3, log.Length);
        Assert.Equal(["2026-11-23T09:40:00Z", "nightly/2026-10-20", "2"], log[1].Split('\t')[..3]);
        Assert.EndsWith("\t_to_delete/datamove1123/nightly/2026-10-20", log[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_outside_the_five_folders_and_a_target_outside_the_two_are_refused()
    {
        Put("queue/old/a.txt", "a");
        var archiver = new MountArchiver(new ShareFolder(root));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => archiver.CountAsync(new ArchiveUnit("queue", "old", "queue/old", []), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => archiver.MoveAsync(new ArchiveUnit("nightly", "x", "nightly/../queue/old", []), "_to_delete/datamove1123/nightly", CancellationToken.None));
        Put("nightly/2026-10-20/a.txt", "a");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => archiver.MoveAsync(ArchiveUnits.DayUnit(ArchiveUnits.Nightly, new DateOnly(2026, 10, 20)), "_to_delete/2026-10-24", CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(root, "nightly/2026-10-20/a.txt")));
    }
}
