using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The funnel by channel in SQL: the card view counts touches, replies and rooms a channel a week from the rows view
/// alone and carries counts only; the rows view names the six channels, reads only columns its tables declare, and
/// joins a reply or a room to the newest touch at the same company.
/// </summary>
public sealed partial class ChannelViewTests
{
    [Fact]
    public void The_card_view_counts_a_channel_a_week_from_the_rows_view_and_carries_counts_only()
    {
        string card = File.ReadAllText(Database("Views", "CardChannels.sql"));
        Assert.Contains("CREATE VIEW [card].[Channels]", card, StringComparison.Ordinal);
        Assert.Contains("FROM [jobhunt].[ChannelRows] c", card, StringComparison.Ordinal);
        foreach (string column in new[] { "[Channel]", "[Week]", "[Touches]", "[Replies]", "[Rooms]" })
        {
            Assert.Contains(column, card, StringComparison.Ordinal);
        }
        Assert.Single(Regex.Matches(card, @"\bFROM\b"));
        Assert.DoesNotContain((char)0x2014, card);
    }

    [Fact]
    public void The_rows_view_names_the_six_channels_and_reads_only_declared_columns()
    {
        string rows = File.ReadAllText(Database("Views", "ChannelRows.sql"));
        Assert.Contains("CREATE VIEW [jobhunt].[ChannelRows]", rows, StringComparison.Ordinal);
        foreach (string channel in new[] { "'pounce'", "'hotmgr'", "'email campaign'", "'old contacts'", "'applications'", "'recruiters'" })
        {
            Assert.Contains(channel, rows, StringComparison.Ordinal);
        }
        var tracker = Columns("Tracker");
        var pounce = Columns("PounceRoles");
        foreach (Match used in Qualified().Matches(rows))
        {
            var table = used.Groups[1].Value == "t" ? tracker : used.Groups[1].Value == "p" ? pounce : null;
            Assert.True(table is null || table.Contains(used.Groups[2].Value), $"{used.Value} is not a column its table declares");
        }
        Assert.Contains("x.[Company] = k.[Company] AND x.[Day] <= k.[Day] ORDER BY x.[Day] DESC", rows, StringComparison.Ordinal);
        Assert.DoesNotContain((char)0x2014, rows);
    }

    private static string Database(params string[] parts) => Path.Combine([Root(), "functions", "JobHunt.Database", .. parts]);

    private static HashSet<string> Columns(string table) =>
        Column().Matches(File.ReadAllText(Database("Tables", table + ".sql"))).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^\s*\[(\w+)\]", RegexOptions.Multiline)]
    private static partial Regex Column();

    [GeneratedRegex(@"\b([tp])\.\[(\w+)\]")]
    private static partial Regex Qualified();
}
