using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace JobHunt.Functions.Merge;

/// <summary>
/// The merger's files on the Azure Files share, read and written over HTTPS with the same
/// connection the rest of this app uses. <paramref name="root"/> is a folder inside the share the
/// merger treats as its job-hunt folder: empty for the real folder, a scratch folder for a test.
/// </summary>
public sealed class ShareMergeStore(ShareClient share, string root) : IMergeStore
{
    private const int MaxRange = 4 * 1024 * 1024;

    public async Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        (await File(path).ExistsAsync(ct)).Value;

    public async Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        var download = await File(path).DownloadAsync(cancellationToken: ct);
        using var buffer = new MemoryStream();
        await download.Value.Content.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    public async Task<IReadOnlyList<MergeEntry>> ListFilesAsync(string folder, CancellationToken ct)
    {
        var entries = new List<MergeEntry>();
        var options = new ShareDirectoryGetFilesAndDirectoriesOptions { Traits = ShareFileTraits.Timestamps };
        try
        {
            await foreach (ShareFileItem item in Folder(folder).GetFilesAndDirectoriesAsync(options, ct))
            {
                if (item.IsDirectory) { continue; }
                DateTimeOffset written = item.Properties?.LastWrittenOn ?? item.Properties?.LastModified ?? DateTimeOffset.MinValue;
                entries.Add(new MergeEntry(item.Name, written, item.FileSize ?? 0));
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
        return entries;
    }

    public async Task EnsureFolderAsync(string folder, CancellationToken ct)
    {
        string path = string.Empty;
        foreach (string part in Full(folder).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = path.Length == 0 ? part : $"{path}/{part}";
            await share.GetDirectoryClient(path).CreateIfNotExistsAsync(cancellationToken: ct);
        }
    }

    public async Task WriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        ShareFileClient file = File(path);
        await file.CreateAsync(bytes.Length, cancellationToken: ct);
        await UploadAsync(file, bytes, 0, ct);
    }

    public async Task ReplaceAsync(string tempPath, string path, CancellationToken ct) =>
        await File(tempPath).RenameAsync(Full(path), new ShareFileRenameOptions { ReplaceIfExists = true }, cancellationToken: ct);

    public async Task MoveAsync(string path, string destination, CancellationToken ct) =>
        await File(path).RenameAsync(Full(destination), new ShareFileRenameOptions { ReplaceIfExists = false }, cancellationToken: ct);

    public async Task AppendAsync(string path, byte[] bytes, CancellationToken ct)
    {
        ShareFileClient file = File(path);
        long start = 0;
        if ((await file.ExistsAsync(ct)).Value)
        {
            start = (await file.GetPropertiesAsync(cancellationToken: ct)).Value.ContentLength;
            await file.SetHttpHeadersAsync(new ShareFileSetHttpHeadersOptions { NewSize = start + bytes.Length }, cancellationToken: ct);
        }
        else
        {
            await file.CreateAsync(bytes.Length, cancellationToken: ct);
        }
        await UploadAsync(file, bytes, start, ct);
    }

    private static async Task UploadAsync(ShareFileClient file, byte[] bytes, long offset, CancellationToken ct)
    {
        for (int done = 0; done < bytes.Length; done += MaxRange)
        {
            int length = Math.Min(MaxRange, bytes.Length - done);
            using var chunk = new MemoryStream(bytes, done, length, writable: false);
            await file.UploadRangeAsync(new HttpRange(offset + done, length), chunk, cancellationToken: ct);
        }
    }

    private string Full(string path) =>
        string.IsNullOrEmpty(root) ? path.Trim('/') : $"{root.Trim('/')}/{path.Trim('/')}";

    private ShareDirectoryClient Folder(string folder)
    {
        string full = Full(folder);
        return full.Length == 0 ? share.GetRootDirectoryClient() : share.GetDirectoryClient(full);
    }

    private ShareFileClient File(string path)
    {
        string full = Full(path);
        int slash = full.LastIndexOf('/');
        ShareDirectoryClient folder = slash < 0 ? share.GetRootDirectoryClient() : share.GetDirectoryClient(full[..slash]);
        return folder.GetFileClient(full[(slash + 1)..]);
    }
}
