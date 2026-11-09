using System.Text;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Microsoft.Extensions.Logging;

namespace JobHunt.Infrastructure.Shared.Share;

/// <summary>
/// The opening numbers of the night's bookend, written to nightly\bookend-yyyy-MM-dd.md on the
/// share. The CSV is counted here. The inbox and the Sheet are counted by the nightly lane
/// until the Gmail and Sheets credentials exist as settings; each line says which. It also reads whether the
/// night's reply run wrote its queue, which the 5 o'clock hour asks before it dispatches the reply lane again.
/// </summary>
public sealed class Bookend(ShareClient share, ILogger<Bookend> log)
{
    public async Task<string> OpenAsync(DateTime local, IReadOnlyList<string> earlier, CancellationToken ct)
    {
        string rows = await CsvRowsAsync(ct);
        var text = new StringBuilder()
            .AppendLine($"# Bookend {local:yyyy-MM-dd}")
            .AppendLine()
            .AppendLine($"Opened {local:HH:mm} America/Chicago by the 4:00 AM Function, no model.")
            .AppendLine()
            .AppendLine("## Opening")
            .AppendLine($"- Job_Hunt_Tracker.csv data rows: {rows}")
            .AppendLine("- Gmail inbox: not read by the Function (no Gmail credential in its settings); the nightly lane reads it.")
            .AppendLine("- Google Sheet rows: not read by the Function (no Sheets credential in its settings); the nightly lane reads it.");
        foreach (string line in earlier) { text.AppendLine($"- {line}"); }
        text.AppendLine().AppendLine("## Closing").AppendLine("- written by the nightly lane");

        var dir = share.GetDirectoryClient("nightly");
        await dir.CreateIfNotExistsAsync(cancellationToken: ct);
        var file = dir.GetFileClient($"bookend-{local:yyyy-MM-dd}.md");
        byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
        await file.CreateAsync(bytes.Length, cancellationToken: ct);
        using var stream = new MemoryStream(bytes);
        await file.UploadAsync(stream, cancellationToken: ct);
        log.LogInformation("bookend opened, CSV rows {Rows}", rows);
        return rows;
    }

    /// <summary>The day's reply run folders under nightly/&lt;date&gt;/, each with whether it holds linkedin-queue.md.</summary>
    /// <param name="local">The day, in Central time.</param>
    /// <param name="ct">Stops the listing.</param>
    public async Task<IReadOnlyList<(string Folder, bool HasQueue)>> ReplyRunsAsync(DateTime local, CancellationToken ct)
    {
        var day = share.GetDirectoryClient($"nightly/{local:yyyy-MM-dd}");
        var runs = new List<(string, bool)>();
        if (!(await day.ExistsAsync(ct)).Value)
        {
            return runs;
        }
        await foreach (ShareFileItem item in day.GetFilesAndDirectoriesAsync(cancellationToken: ct))
        {
            if (item.IsDirectory && ReplyRetry.IsRun(item.Name))
            {
                runs.Add((item.Name, (await day.GetSubdirectoryClient(item.Name).GetFileClient(ReplyRetry.Queue).ExistsAsync(ct)).Value));
            }
        }
        return runs;
    }

    private async Task<string> CsvRowsAsync(CancellationToken ct)
    {
        var file = share.GetRootDirectoryClient().GetFileClient("Job_Hunt_Tracker.csv");
        if (!(await file.ExistsAsync(ct)).Value) { return "not read: Job_Hunt_Tracker.csv is not on the share"; }
        var download = await file.DownloadAsync(cancellationToken: ct);
        using var reader = new StreamReader(download.Value.Content, Encoding.UTF8);
        return TrackerCsv.DataRows(await reader.ReadToEndAsync(ct)).ToString();
    }
}
