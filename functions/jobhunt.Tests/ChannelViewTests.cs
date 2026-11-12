using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The funnel by channel in SQL: the card view counts touches, replies and rooms a channel a week from the rows view
/// alone and carries counts only; the rows view names the six channels, reads only columns its tables declare, and
/// joins a reply or a room to the newest touch at the same company; an application takes applications only when no
/// channel above it matched, and a recruiter is one whose person or note says recruiter or talent.
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

    [Fact]
    public void An_application_takes_applications_only_when_no_channel_above_it_matched()
    {
        string rows = File.ReadAllText(Database("Views", "ChannelRows.sql"));
        string touches = rows[rows.IndexOf("[Touches] AS (", StringComparison.Ordinal)..];
        Assert.Contains("WHEN k.[Kind] = 'application' AND (k.[Channel] IS NULL OR k.[Channel] = 'hotmgr') THEN 'applications'", touches, StringComparison.Ordinal);
        Assert.DoesNotContain("k.[Channel] <> 'pounce'", touches, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recruiter_is_one_whose_person_or_note_says_recruiter_or_talent()
    {
        string recruiter = File.ReadAllLines(Database("Views", "ChannelRows.sql")).Single(line => line.Contains("THEN 'recruiters'", StringComparison.Ordinal));
        foreach (string test in new[] { "r.[Who] LIKE '%RECRUIT%'", "r.[Who] LIKE '%TALENT%'", "r.[Note] LIKE '%RECRUITER%'", "r.[Note] LIKE '%TALENT%'" })
        {
            Assert.Contains(test, recruiter, StringComparison.Ordinal);
        }
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
