// The catalogue port's typed words: a word of three characters or more is a range on the
// CatalogueWords table's word index, and the table is filled beside the Catalogue table. A file of
// its own because EfVehicleCatalogue.cs holds the other queries, and whether the words are there
// yet is a question of its own (ADR: The store does the work, the addendum on a typed word as a seek).
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Infrastructure;

/// <summary>The words table's reads and fill; the class is described in EfVehicleCatalogue.cs.</summary>
public sealed partial class EfVehicleCatalogue
{
    // Whether every stored row's words are in CatalogueWords. Until they are, a word is looked for
    // in each row's text, which is slow and answers the same.
    private volatile bool _wordsReady;
    private long _wordsCheckedTicks;
    private int _backfilling;

    /// <summary>The backfill of the words for rows stored before the table existed, while one runs. A test awaits it.</summary>
    public Task? WordsFilling { get; private set; }

    /// <summary>
    /// The rows that hold the token: by the words table when it is filled and the token is a word,
    /// by each row's text otherwise, as the start of a word or, under three characters, anywhere
    /// (VehicleSearchIndex.Holds, which every in-memory filter asks too).
    /// </summary>
    private IQueryable<CatalogueRow> Holding(YardDbContext db, IQueryable<CatalogueRow> rows, string text, bool live, bool upcoming, bool ended, long d)
    {
        if (text.Length >= VehicleSearchIndex.WordLength && _wordsReady && !text.Contains(' ', StringComparison.Ordinal))
        {
            var places = local ? WordsInRange(db, text) : db.CatalogueWords.Where(w => w.Word.StartsWith(text));
            var seqs = places.Select(w => w.Seq);
            return rows.Where(row => seqs.Contains(row.Seq)
                || (live && row.StartOffsetMs <= d && row.EndOffsetMs > d)
                || (upcoming && row.StartOffsetMs > d)
                || (ended && row.EndOffsetMs <= d));
        }
        if (text.Length >= VehicleSearchIndex.WordLength)
        {
            string spaced = " " + text;
            return rows.Where(row => row.SearchText.StartsWith(text) || row.SearchText.Contains(spaced)
                || (live && row.StartOffsetMs <= d && row.EndOffsetMs > d)
                || (upcoming && row.StartOffsetMs > d)
                || (ended && row.EndOffsetMs <= d));
        }
        return rows.Where(row => row.SearchText.Contains(text)
            || (live && row.StartOffsetMs <= d && row.EndOffsetMs > d)
            || (upcoming && row.StartOffsetMs > d)
            || (ended && row.EndOffsetMs <= d));
    }

    /// <summary>
    /// A word's prefix as a range of the index, from the word to the word with its last character
    /// raised by one, because SQLite's LIKE ignores case and so reads no index on a column that
    /// compares as bytes; the words are lowercased, and so is the token.
    /// </summary>
    private static IQueryable<CatalogueWordRow> WordsInRange(YardDbContext db, string text)
    {
        string upper = text[^1] == char.MaxValue ? text + char.MaxValue : text[..^1] + (char)(text[^1] + 1);
        return db.CatalogueWords.Where(w => string.Compare(w.Word, text) >= 0 && string.Compare(w.Word, upper) < 0);
    }

    /// <summary>
    /// Whether the words are filled, read at most once a minute until they are: the last stored
    /// row has its words, because the fill and the backfill both write in the order of the place.
    /// When they are not, the site that fills the catalogue starts the backfill.
    /// </summary>
    private async Task CheckWordsAsync(YardDbContext db, int filled, bool now = false)
    {
        if (_wordsReady)
        {
            return;
        }
        long ticks = DateTime.UtcNow.Ticks;
        if (!now && ticks - Interlocked.Read(ref _wordsCheckedTicks) < TimeSpan.TicksPerMinute)
        {
            return;
        }
        Interlocked.Exchange(ref _wordsCheckedTicks, ticks);
        int last = Math.Min(filled, target) - 1;
        _wordsReady = last < 0 || await db.CatalogueWords.AnyAsync(w => w.Seq == last);
        if (!_wordsReady && local)
        {
            await BackfillFileAsync(last + 1);
        }
        else if (!_wordsReady && fillsWords)
        {
            StartBackfill(last + 1);
        }
    }

    /// <summary>Before a query with a typed word on a site still waiting for the words: look again, once a minute.</summary>
    private async Task WordsBeforeQueryAsync(YardDbContext db, CatalogueFilter filter)
    {
        if (!_wordsReady && filter.Tokens.Count > 0)
        {
            await CheckWordsAsync(db, target);
        }
    }

    /// <summary>The words for a SQLite file's rows stored before the table existed, split here and written at once.</summary>
    private async Task BackfillFileAsync(int filled)
    {
        await using var db = await contexts.CreateDbContextAsync();
        var held = (await db.CatalogueWords.Select(w => w.Seq).Distinct().ToListAsync()).ToHashSet();
        for (int low = 0; low < filled; low += 5_000)
        {
            var rows = await db.Catalogue.AsNoTracking()
                .Where(row => row.Seq >= low && row.Seq < low + 5_000)
                .Select(row => new { row.Seq, row.SearchText })
                .ToListAsync();
            await FillWordsFileAsync([.. rows.Where(row => !held.Contains(row.Seq)).SelectMany(row => CatalogueWords.For(row.Seq, row.SearchText))]);
        }
        _wordsReady = true;
    }

    /// <summary>
    /// The words for Azure SQL Database's rows stored before the table existed, beside the site:
    /// split by the server a range of places at a time with a fifth of a second between, so the
    /// backfill leaves the Basic tier's log rate for visitors. One at a time; a backfill that
    /// failed is started again by the next minute's check.
    /// </summary>
    private void StartBackfill(int filled)
    {
        if (Interlocked.Exchange(ref _backfilling, 1) == 1)
        {
            return;
        }
        WordsFilling = Task.Run(async () =>
        {
            try
            {
                for (int low = 0; low < filled; low += CatalogueWords.BackfillSpan)
                {
                    await using var db = await contexts.CreateDbContextAsync();
                    db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
                    await db.Database.ExecuteSqlRawAsync(
                        CatalogueWords.BackfillSql,
                        new SqlParameter("@low", low),
                        new SqlParameter("@high", Math.Min(filled, low + CatalogueWords.BackfillSpan)));
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }
                _wordsReady = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Left for the next minute's check to start again; the rows written so far stay.
            }
            finally
            {
                Interlocked.Exchange(ref _wordsCheckedTicks, 0);
                Interlocked.Exchange(ref _backfilling, 0);
            }
        });
    }

    /// <summary>A SQLite file's word rows, in one transaction and one prepared INSERT OR IGNORE a row.</summary>
    private async Task FillWordsFileAsync(IReadOnlyList<CatalogueWordRow> words)
    {
        if (words.Count == 0)
        {
            return;
        }
        await using var db = await contexts.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await WriteWordsAsync(connection, transaction, words);
        await transaction.CommitAsync();
    }

    /// <summary>The prepared INSERT OR IGNORE of word rows, inside a transaction the caller holds.</summary>
    private static async Task WriteWordsAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, IEnumerable<CatalogueWordRow> words)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO \"CatalogueWords\" (\"Seq\", \"Word\") VALUES ($seq, $word)";
        var seq = command.CreateParameter();
        seq.ParameterName = "$seq";
        command.Parameters.Add(seq);
        var word = command.CreateParameter();
        word.ParameterName = "$word";
        command.Parameters.Add(word);
        foreach (var row in words)
        {
            seq.Value = row.Seq;
            word.Value = row.Word;
            await command.ExecuteNonQueryAsync();
        }
    }
}
