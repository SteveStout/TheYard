using System.Security.Cryptography;
using System.Text;
using JobHunt.Server.Infrastructure.Shared.Cli;
using JobHunt.Server.Infrastructure.Shared.Share;

namespace JobHunt.Server.Tests;

/// <summary>RULE 76's resume fix: a tailored build that stops falls back to the locked send copy, checked by its hash.</summary>
public sealed class ResumeLockTests : IDisposable
{
    private const string Failure = "STOP. check_resume_layout.py failed on more than the known white-text flag:";
    private const string Bytes = "%PDF-1.7 the send copy";

    // The temp folder stands in for the share root: hashing real bytes on disk is this adapter's own job (RULE 79 check 2).
    private readonly string folder = Directory.CreateTempSubdirectory("server6am-lock-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Lock(string pdfSha) =>
        "# RESUME LOCK, 2026-11-10.\n\n## THE FILE OF RECORD\n\n"
        + "    Steven_Stout_Resume_2026-11-10.docx     SOURCE OF TRUTH. Edit this, rebuild the PDF.\n"
        + "    Steven_Stout_Resume_2026-11-10.pdf      ATTACH THIS. 63,956 bytes.\n"
        + "    StevenStoutResume_2026-11-10.pdf        THE SEND COPY, same bytes, same hash as the PDF above.\n\n"
        + "NUMBERS: 2,062 xUnit, 631 Vitest and 218 Playwright tests\n"
        + $"SHA-256: docx 40ce1d72d73f6d7c2b0a4f9c47fe20bd5b12e38812b833b465e958636bd86129 pdf {pdfSha}\n";

    private async Task<ResumeBuild> BuildAsync(string? failure, string lockSha, string bytes = Bytes)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "RESUME-LOCK-2026-11-05.md"), Lock(new string('0', 64)));
        await File.WriteAllTextAsync(Path.Combine(folder, "RESUME-LOCK-2026-11-10.md"), Lock(lockSha));
        await File.WriteAllTextAsync(Path.Combine(folder, "StevenStoutResume_2026-11-10.pdf"), bytes);
        var tailored = new FakeResumes { Failure = failure };
        return await new LockedSendCopy(tailored, new ShareFolder(folder)).BuildAsync("Staff Engineer", "Acme", "/out", default);
    }

    [Fact]
    public void The_newest_lock_is_read_and_its_send_copy_and_pdf_hash_come_back()
    {
        Assert.Equal("RESUME-LOCK-2026-11-10.md", ResumeLock.Newest(["RESUME-LOCK-2026-09-03.md", "RESUME-LOCK-2026-11-10.md", "RESUME-LOCK-2026-11-05.md", "RESUME-LOCK-notes.md"]));
        Assert.Equal(new LockedCopy("L", "StevenStoutResume_2026-11-10.pdf", Sha(Bytes)), ResumeLock.Read("L", Lock(Sha(Bytes).ToUpperInvariant())));
    }

    [Fact]
    public async Task A_title_that_fails_the_layout_gate_goes_out_with_the_locked_copy()
    {
        ResumeBuild built = await BuildAsync(Failure, Sha(Bytes));
        Assert.Null(built.Failure);
        Assert.Equal(Path.Combine(new ShareFolder(folder).Root, "StevenStoutResume_2026-11-10.pdf"), built.Path);
        Assert.Equal(Sha(Bytes), built.Sha);
        Assert.Equal($"locked send copy (tailored build failed: {Failure})", built.Headline);
    }

    [Fact]
    public async Task A_title_that_passes_keeps_its_tailored_build()
    {
        ResumeBuild built = await BuildAsync(null, Sha(Bytes));
        Assert.Equal(new ResumeBuild("/out/StevenStoutResume_2026-10-29.pdf", "STAFF ENGINEER", "abc123"), built);
    }

    [Fact]
    public async Task A_send_copy_whose_bytes_do_not_match_the_lock_fails_loudly_and_attaches_nothing()
    {
        ResumeBuild built = await BuildAsync(Failure, Sha(Bytes), "%PDF-1.7 somebody else's bytes");
        Assert.Contains("LOCK MISMATCH", built.Failure, StringComparison.Ordinal);
        Assert.Equal(string.Empty, built.Path);
    }

    [Fact]
    public void No_lock_and_no_file_each_say_so_and_attach_nothing()
    {
        Assert.Contains("no RESUME-LOCK file", ResumeLock.Fallback(Failure, null, string.Empty, null).Failure, StringComparison.Ordinal);
        ResumeBuild missing = ResumeLock.Fallback(Failure, new LockedCopy("RESUME-LOCK-2026-11-10.md", "StevenStoutResume_2026-11-10.pdf", Sha(Bytes)), "/x", null);
        Assert.Contains("is missing", missing.Failure, StringComparison.Ordinal);
        Assert.Equal(string.Empty, missing.Path);
    }

    [Fact]
    public void The_tracker_row_names_the_fallback_and_the_reason()
    {
        string line = ResumeLock.Fallback(Failure, new LockedCopy("L", "StevenStoutResume_2026-11-10.pdf", "ab"), "/mnt/j/StevenStoutResume_2026-11-10.pdf", "ab").Headline;
        Assert.StartsWith(ResumeLock.Marker, line, StringComparison.Ordinal);
        Assert.Contains("tailored build failed: STOP.", line, StringComparison.Ordinal);
    }
}
