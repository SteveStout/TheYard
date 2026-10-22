using System.Text;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions.Tests;

/// <summary>A throwaway job-hunt folder for one test: a master, an optional moved-out file, and spool files.</summary>
public sealed class Scratch : IDisposable
{
    public const string Header = "Count,Person,Company,Communication,Date,Phone/Email,Notes";

    public Scratch()
    {
        Root = Path.Combine(Path.GetTempPath(), "merger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "tracker-inbox"));
        Store = new FolderMergeStore(Root);
    }

    public string Root { get; }

    public FolderMergeStore Store { get; }

    public void Master(string body, bool bom = false) => Write(MergePaths.Master, Header + "\r\n" + body, bom);

    public void MovedOut(string body) => Write(MergePaths.MovedOut, Header + "\r\n" + body);

    public void Spool(string name, string text) => Write($"tracker-inbox/{name}", text);

    public void Write(string path, string text, bool bom = false)
    {
        string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        byte[] body = Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(full, bom ? [0xEF, 0xBB, 0xBF, .. body] : body);
    }

    public byte[] Bytes(string path) => File.ReadAllBytes(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));

    public string Text(string path) => TrackerCsvText.Decode(Bytes(path));

    public bool Has(string path) => File.Exists(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));

    public Task<int> RunAsync() => new TrackerMerger(Store, TimeProvider.System).RunPassAsync(CancellationToken.None);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }
}
