namespace JobHunt.Application.Features.NetworkLog;

/// <summary>What one import of the 4 AM lane's TopicLines did.</summary>
/// <param name="Applied">Lines stored on their rows.</param>
/// <param name="Refused">Lines that failed the rule, each with its key and the check.</param>
public sealed record TopicImportResult(int Applied, IReadOnlyList<string> Refused);

/// <summary>
/// The 4 AM lane's TopicLines into their rows: the lane cannot write SQL, so it writes networklog-topics.tsv in its
/// own run folder and this reads the last three Central days of them, oldest first, so the newest line for a person
/// wins. A line already stored from the same file, or a row whose line came from a newer file, is left as it is, so a
/// re-read changes nothing and a later file can correct an earlier one.
/// </summary>
public sealed class TopicImport(IConversationStore rows, IMergeStore share)
{
    /// <summary>The file the 4 AM lane writes in its run folder.</summary>
    public const string FileName = "networklog-topics.tsv";

    /// <summary>The Central days read, today included.</summary>
    public const int Days = 3;

    /// <summary>Reads the lane's files for the days up to <paramref name="centralToday"/> and stores the lines that pass.</summary>
    public async Task<TopicImportResult> RunAsync(DateOnly centralToday, CancellationToken ct)
    {
        var topics = new List<TopicRow>();
        for (int back = Days - 1; back >= 0; back--)
        {
            string day = $"nightly/{centralToday.AddDays(-back):yyyy-MM-dd}";
            foreach (string run in (await share.ListFoldersAsync(day, ct)).Where(name => name.Contains("-reply-", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                string path = $"{day}/{run}/{FileName}";
                if (await share.ExistsAsync(path, ct))
                {
                    topics.AddRange(TopicLineRule.Parse(TrackerCsvText.Decode(await share.ReadAsync(path, ct)), path));
                }
            }
        }
        int applied = 0;
        var refused = new List<string>();
        foreach (var topic in topics.GroupBy(t => t.PersonKey, StringComparer.Ordinal).Select(g => g.Last()))
        {
            if (await rows.FindAsync(topic.PersonKey, ct) is not { } row)
            {
                refused.Add($"{topic.PersonKey}: no such row");
                continue;
            }
            if (row.TopicSource is { } had && string.CompareOrdinal(had, topic.Source) >= 0)
            {
                continue;
            }
            if (TopicLineRule.Fails(topic.TopicLine, row.LastSubject) is { } check)
            {
                refused.Add($"{topic.PersonKey}: {check}");
                continue;
            }
            await rows.SetTopicAsync(row.Id, topic.TopicLine.TrimEnd('.'), topic.Source, ct);
            applied++;
        }
        return new TopicImportResult(applied, refused);
    }
}
