using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Tests FileBrowser's upload: only the file name is ever written (a path in the name is dropped),
/// an existing name is refused unless the caller asks to overwrite, and a file past the limit is
/// refused before it is written into home. The form parser has read the body by then, and the web
/// server cuts off a request past its own limit with a 413.
/// </summary>
public sealed class FileUploadTests : SampleTree
{
    [Fact]
    public async Task Upload_writes_only_the_file_name_never_a_path()
    {
        using var content = new MemoryStream(new byte[10]);
        FileEntry entry = await Browser.UploadAsync("docs", "../../evil.txt", 10, content, overwrite: false, CancellationToken.None);
        Assert.Equal("docs/evil.txt", entry.Path);
        Assert.Equal(EntryKind.File, Store.KindOf(Path.Combine(Root, "docs", "evil.txt")));
    }

    [Fact]
    public async Task Upload_refuses_an_existing_name_unless_told_to_overwrite()
    {
        using var content = new MemoryStream(new byte[5]);
        var problem = await Assert.ThrowsAsync<ApiRefusalException>(() => Browser.UploadAsync("docs", "readme.md", 5, content, overwrite: false, CancellationToken.None));
        Assert.Equal(409, problem.Status);
        content.Position = 0;
        FileEntry entry = await Browser.UploadAsync("docs", "readme.md", 5, content, overwrite: true, CancellationToken.None);
        Assert.Equal(5, entry.SizeBytes);
    }

    /// <summary>
    /// A visitor who closes the tab mid-upload stops the copy. Until the browser took the request's
    /// cancellation, every byte the client had already sent was written to its end, so an abandoned
    /// upload of a large file still cost the disk and the quota the whole file.
    ///
    /// <para>What a stopped copy leaves is the file, empty: the target is created before the first
    /// byte is read, so the stop costs an empty name in the folder rather than ten bytes of a file
    /// nobody asked for. That is what is asserted, because asserting that nothing at all is there
    /// would be asserting a rollback this code does not do and should not pretend to.</para>
    /// </summary>
    [Fact]
    public async Task Upload_the_visitor_walked_away_from_is_not_copied_to_its_end()
    {
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();
        using var content = new MemoryStream(new byte[10]);
        string target = Path.Combine(Root, "docs", "half.bin");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Browser.UploadAsync("docs", "half.bin", 10, content, overwrite: false, gone.Token));

        Assert.Equal(EntryKind.File, Store.KindOf(target));
        Assert.Equal(0, Store.Describe(target).SizeBytes);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public async Task Upload_past_the_limit_is_413_before_the_file_is_written()
    {
        var options = new FilesOptions { MaxUploadBytes = 4 };
        var browser = new FileBrowser(new HomePath(Root), Store, options, new HomeQuota(new HomePath(Root), Store, options));
        using var content = new MemoryStream(new byte[5]);
        var problem = await Assert.ThrowsAsync<ApiRefusalException>(() => browser.UploadAsync("", "big.bin", 5, content, overwrite: false, CancellationToken.None));
        Assert.Equal(413, problem.Status);
        Assert.Equal(0, content.Position);
    }
}
