// The CatalogueWords table: one row per word of each vehicle's searchable text, so a typed word is
// an index seek instead of a scan of every row's text. Its own file because the table, its fill and
// the SQL that fills it from the Catalogue table on Azure SQL Database are one subject
// (ADR: The store does the work, the addendum on a typed word as a seek).
using TheYard.Domain;

namespace TheYard.Infrastructure;

/// <summary>One word of one vehicle's searchable text, at that vehicle's place in the catalogue.</summary>
public sealed class CatalogueWordRow
{
    /// <summary>The vehicle's place in the expansion, the Catalogue table's Seq.</summary>
    public required int Seq { get; set; }

    /// <summary>One word of the vehicle's lowercased searchable text (VehicleSearchIndex.WordsOf).</summary>
    public required string Word { get; set; }
}

/// <summary>The words of a catalogue row, and the statement that writes them for rows already stored.</summary>
public static class CatalogueWords
{
    /// <summary>The longest word: the widest searched column, since a word never spans a space.</summary>
    public const int WordLength = 64;

    /// <summary>How many places one statement of the backfill covers on Azure SQL Database.</summary>
    public const int BackfillSpan = 2_000;

    /// <summary>The rows for one vehicle's words, at most one per distinct word.</summary>
    public static IEnumerable<CatalogueWordRow> For(int seq, string searchText) =>
        VehicleSearchIndex.WordsOf(searchText)
            .Where(word => word.Length <= WordLength)
            .Select(word => new CatalogueWordRow { Seq = seq, Word = word });

    /// <summary>
    /// The words of every stored row from @low up to @high that has none yet, split by SQL Server
    /// itself, so a hundred thousand rows already in the Catalogue table never cross the network to
    /// be split. The NOT EXISTS is a seek on the table's clustered key, which leads with the place,
    /// so a statement that ran before, or ran on the other site at the same minute, writes nothing twice.
    /// </summary>
    public const string BackfillSql = """
        INSERT INTO [dbo].[CatalogueWords] ([Seq], [Word])
        SELECT DISTINCT c.[Seq], s.[value]
        FROM [dbo].[Catalogue] AS c
        CROSS APPLY STRING_SPLIT(c.[SearchText], N' ') AS s
        WHERE c.[Seq] >= @low AND c.[Seq] < @high AND s.[value] <> N'' AND LEN(s.[value]) <= 64
          AND NOT EXISTS (SELECT 1 FROM [dbo].[CatalogueWords] AS w WHERE w.[Seq] = c.[Seq]);
        """;
}
