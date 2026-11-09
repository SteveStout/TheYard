namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// Puts the hot companies first across every queue file and day. A block's bucket is its "- Hot:" line: A a Tier 1 role
/// posted or first seen in the last 72 hours, B the rest of Tier 1, C a company with an application in the last 21 days,
/// D a growth signal re-read within 30 days, E everything else and every block with no line. A block that would read D
/// or E reads C when the tracker holds an application at that company inside 21 days, because Rule 58 check 4 gives that
/// room a name the same night. Inside a bucket the order is the file's, then the block's, as the queue reads.
/// </summary>
public static class HotRank
{
    /// <summary>How many days back an application promotes a block to C.</summary>
    public const int RecentDays = 21;

    private const string Buckets = "ABCDE";

    /// <summary>The bucket a block ranks in today.</summary>
    /// <param name="block">The queue block.</param>
    /// <param name="rows">Tracker rows, master and spool, seven cells each.</param>
    /// <param name="today">The Central day.</param>
    public static char Bucket(QueueBlock block, IReadOnlyList<string[]> rows, DateOnly today)
    {
        char said = block.Hot.Length == 1 && Buckets.Contains(char.ToUpperInvariant(block.Hot[0]), StringComparison.Ordinal) ? char.ToUpperInvariant(block.Hot[0]) : 'E';
        bool applied = ApplicationCap.Applications(block.Company, rows, today).Any(application => today.DayNumber - application.Day.DayNumber <= RecentDays);
        return said is 'D' or 'E' && applied ? 'C' : said;
    }

    /// <summary>The blocks hottest first, the queue's own order kept inside each bucket.</summary>
    /// <param name="blocks">The open blocks, in file then block order.</param>
    /// <param name="rows">Tracker rows, master and spool, seven cells each.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<QueueBlock> Order(IEnumerable<QueueBlock> blocks, IReadOnlyList<string[]> rows, DateOnly today) =>
        [.. blocks.Select((block, index) => (block, index)).OrderBy(pair => Bucket(pair.block, rows, today)).ThenBy(pair => pair.index).Select(pair => pair.block)];
}
