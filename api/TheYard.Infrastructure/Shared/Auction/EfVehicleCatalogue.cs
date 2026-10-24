// The catalogue port over the Catalogue table: a filter and an order translated into one EF Core
// query each, paged in the database, tracking nothing. Its own file because this is one of the two
// query implementations ADR: The store does the work fences, and the other is its Cosmos DB twin.
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Shared.Auction;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>
/// <see cref="IVehicleCatalogue"/> on SQL Server or SQLite. A database filled further than this
/// site's catalogue answers as this catalogue, because every query then starts from
/// <c>Seq &lt; target</c>; a database that holds exactly the catalogue, which is every one this
/// site fills, leaves that out, so an order or a count is served off its own index rather than a
/// range over the clustered key. Every read is <c>AsNoTracking</c>, because nothing here is written back.
/// </summary>
/// <param name="contexts">A context per operation.</param>
/// <param name="target">How many vehicles the catalogue is.</param>
/// <param name="local">True for a SQLite file this process owns, which is filled at warm-up whatever its size.</param>
/// <param name="fillsWords">True for the site that fills this store's catalogue, which also fills the words of rows stored before the words table existed.</param>
public sealed partial class EfVehicleCatalogue(IDbContextFactory<YardDbContext> contexts, int target, bool local, bool fillsWords = false) : IVehicleCatalogue
{
    /// <inheritdoc />
    public int Target => target;

    /// <summary>
    /// A local file fills whole at warm-up in batches of five thousand; Azure SQL Database fills
    /// a thousand rows at a time with a fifth of a second between, beside the site, past five
    /// thousand missing, so a fill on the Basic tier leaves the log rate for visitors.
    /// </summary>
    public CatalogueFillPace Pace { get; } = local
        ? new CatalogueFillPace(5_000, TimeSpan.Zero, int.MaxValue)
        : new CatalogueFillPace(1_000, TimeSpan.FromMilliseconds(200), 5_000);

    /// <summary>The offset from which a page reads its places off the index first and its rows after.</summary>
    public const int DeepSkip = 1_000;

    // Whether the table holds vehicles past this catalogue, assumed so until it has been counted.
    private volatile bool _beyond = true;

    /// <inheritdoc />
    public async Task<int> FilledAsync()
    {
        await using var db = await contexts.CreateDbContextAsync();
        int filled = await db.Catalogue.CountAsync(row => row.Seq < target);
        _beyond = await db.Catalogue.AnyAsync(row => row.Seq >= target);
        await CheckWordsAsync(db, filled, now: true);
        return filled;
    }

    /// <summary>Inserts the batch's rows the table does not already hold, in one save.</summary>
    public async Task FillAsync(IReadOnlyList<CatalogueEntry> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }
        // A word check after a fill looks again at once, so the words written with these rows are read.
        Interlocked.Exchange(ref _wordsCheckedTicks, 0);
        if (local)
        {
            await FillFileAsync(batch);
            return;
        }
        await using var db = await contexts.CreateDbContextAsync();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        int low = batch.Min(entry => entry.Seq);
        int high = batch.Max(entry => entry.Seq);
        var held = (await db.Catalogue.AsNoTracking()
            .Where(row => row.Seq >= low && row.Seq <= high)
            .Select(row => row.Seq)
            .ToListAsync()).ToHashSet();
        var rows = batch.Where(entry => !held.Contains(entry.Seq)).Select(entry => entry.ToCatalogueRow()).ToList();
        db.Catalogue.AddRange(rows);
        // Each row's words in the same save, so a row is never stored without them.
        db.CatalogueWords.AddRange(rows.SelectMany(row => CatalogueWords.For(row.Seq, row.SearchText)));
        await db.SaveChangesAsync();
    }

    // #region ef-catalogue-query
    /// <summary>
    /// The filter as a WHERE clause: the store's own spellings compared with equality, the
    /// segment as a comparison of the slot with now, every typed word as a range on the words
    /// table (EfVehicleCatalogue.Words.cs) or a status word that names the row's segment, and the ids somebody's
    /// bid moved left out as one JSON parameter, however many there are.
    /// </summary>
    private IQueryable<CatalogueRow> Query(YardDbContext db, CatalogueFilter filter)
    {
        long d = filter.SinceAnchorMs;
        var rows = db.Catalogue.AsNoTracking();
        if (_beyond)
        {
            rows = rows.Where(row => row.Seq < target);
        }
        if (filter.Make is { } make)
        {
            rows = rows.Where(row => row.Make == make);
        }
        if (filter.BodyStyle is { } bodyStyle)
        {
            rows = rows.Where(row => row.BodyStyle == bodyStyle);
        }
        if (filter.TitleStatus is { } titleStatus)
        {
            rows = rows.Where(row => row.TitleStatus == titleStatus);
        }
        if (filter.Province is { } province)
        {
            rows = rows.Where(row => row.Province == province);
        }
        // A typed word is looked for in every row, so on a SQLite file the schedule's columns are
        // then compared as "+ 0", which no index serves: SQLite's planner, which has no statistics,
        // reads the narrow words index alone rather than ranging over a schedule index and reading
        // every row in the range for its text. SQL Server's planner has statistics and is left to them.
        rows = (filter.Segment, local && filter.Tokens.Count > 0) switch
        {
            (CatalogueSegment.Live, false) => rows.Where(row => row.StartOffsetMs <= d && row.EndOffsetMs > d),
            (CatalogueSegment.Upcoming, false) => rows.Where(row => row.StartOffsetMs > d),
            (CatalogueSegment.Ended, false) => rows.Where(row => row.EndOffsetMs <= d),
            (CatalogueSegment.Live, true) => rows.Where(row => row.StartOffsetMs + 0 <= d && row.EndOffsetMs + 0 > d),
            (CatalogueSegment.Upcoming, true) => rows.Where(row => row.StartOffsetMs + 0 > d),
            (CatalogueSegment.Ended, true) => rows.Where(row => row.EndOffsetMs + 0 <= d),
            _ => rows,
        };
        if (filter.MinCondition is { } grade)
        {
            rows = rows.Where(row => row.ConditionGrade >= grade);
        }
        if (filter.PriceMin is { } low)
        {
            rows = rows.Where(row => row.Price >= low);
        }
        if (filter.PriceMax is { } high)
        {
            rows = rows.Where(row => row.Price <= high);
        }
        foreach (var token in filter.Tokens)
        {
            string text = token.Text;
            bool live = token.AlsoMatches.Contains(CatalogueSegment.Live);
            bool upcoming = token.AlsoMatches.Contains(CatalogueSegment.Upcoming);
            bool ended = token.AlsoMatches.Contains(CatalogueSegment.Ended);
            rows = Holding(db, rows, text, live, upcoming, ended, d);
        }
        if (filter.Exclude.Count > 0)
        {
            string[] moved = [.. filter.Exclude];
            rows = rows.Where(row => !EF.Parameter(moved).Contains(row.Id));
        }
        return rows;
    }

    /// <summary>The order as an ORDER BY that one index serves, the place breaking every tie.</summary>
    private static IQueryable<CatalogueRow> Ordered(IQueryable<CatalogueRow> rows, CatalogueOrder order) => order switch
    {
        CatalogueOrder.EndAscending => rows.OrderBy(row => row.EndOffsetMs).ThenBy(row => row.Seq),
        CatalogueOrder.StartAscending => rows.OrderBy(row => row.StartOffsetMs).ThenBy(row => row.Seq),
        CatalogueOrder.EndDescending => rows.OrderByDescending(row => row.EndOffsetMs).ThenBy(row => row.Seq),
        CatalogueOrder.PriceAscending => rows.OrderBy(row => row.Price).ThenBy(row => row.Seq),
        CatalogueOrder.PriceDescending => rows.OrderByDescending(row => row.Price).ThenBy(row => row.Seq),
        CatalogueOrder.ConditionDescending => rows.OrderByDescending(row => row.ConditionGrade).ThenBy(row => row.Seq),
        CatalogueOrder.BidsDescending => rows.OrderByDescending(row => row.BidCount).ThenBy(row => row.Seq),
        _ => rows.OrderBy(row => row.Seq),
    };

    /// <summary>
    /// The three segments' counts in one statement: one scan of the narrow counts index, three
    /// conditional counts. A filter kept to one segment is counted without the segment in its WHERE
    /// and the other two counts set to nothing here, because the conditional counts already split
    /// the rows by segment, and a segment in the WHERE draws the planner to a range over a schedule
    /// index that then reads every row in it for the other columns the filter names.
    /// </summary>
    public async Task<SegmentCounts> CountAsync(CatalogueFilter filter)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await WordsBeforeQueryAsync(db, filter);
        long d = filter.SinceAnchorMs;
        var counts = await Query(db, filter.In(CatalogueSegment.Any))
            .GroupBy(_ => 1)
            .Select(all => new
            {
                Live = all.Count(row => row.StartOffsetMs <= d && row.EndOffsetMs > d),
                Upcoming = all.Count(row => row.StartOffsetMs > d),
                Ended = all.Count(row => row.EndOffsetMs <= d),
            })
            .FirstOrDefaultAsync();
        if (counts is null)
        {
            return SegmentCounts.None;
        }
        return filter.Segment switch
        {
            CatalogueSegment.Live => new SegmentCounts(counts.Live, 0, 0),
            CatalogueSegment.Upcoming => new SegmentCounts(0, counts.Upcoming, 0),
            CatalogueSegment.Ended => new SegmentCounts(0, 0, counts.Ended),
            _ => new SegmentCounts(counts.Live, counts.Upcoming, counts.Ended),
        };
    }

    /// <summary>One page: the ORDER BY, then OFFSET and FETCH in the database, so it reads only as far as the page.</summary>
    public async Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        if (take <= 0)
        {
            return [];
        }
        await using var db = await contexts.CreateDbContextAsync();
        await WordsBeforeQueryAsync(db, filter);
        bool word = local && filter.Tokens.Count > 0;
        if (!word && skip < DeepSkip)
        {
            var rows = await Ordered(Query(db, filter), order).Skip(skip).Take(take).ToListAsync();
            return [.. rows.Select(row => row.ToEntry())];
        }
        // A page deep in the order, or a typed word on a SQLite file: the page's places first, then
        // its rows by their place. Deep, the places are read off the order's own index, which holds
        // the place, so the rows skipped over are index entries and never the table's rows (an
        // offset of 40,000 took 6.4 seconds on Azure SQL Database read the other way). With a word,
        // the places come off the words index in an order no index serves, so the planner cannot
        // walk an order's index reading every row for its text.
        var places = await (word ? Unindexed(Query(db, filter), order) : Ordered(Query(db, filter), order))
            .Skip(skip).Take(take).Select(row => row.Seq).ToListAsync();
        if (places.Count == 0)
        {
            return [];
        }
        var found = (await db.Catalogue.AsNoTracking().Where(row => EF.Parameter(places).Contains(row.Seq)).ToListAsync())
            .ToDictionary(row => row.Seq);
        return [.. places.Select(place => found[place].ToEntry())];
    }
    // #endregion ef-catalogue-query

    /// <summary>The rows for these ids, by the primary key, in the catalogue's order.</summary>
    public async Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        string[] wanted = [.. ids.Distinct(StringComparer.Ordinal)];
        await using var db = await contexts.CreateDbContextAsync();
        var rows = await db.Catalogue.AsNoTracking()
            .Where(row => EF.Parameter(wanted).Contains(row.Id) && row.Seq < target)
            .OrderBy(row => row.Seq)
            .ToListAsync();
        return [.. rows.Select(row => row.ToEntry())];
    }

    /// <summary>
    /// The four lists of distinct values, read from the 200-row seed table: the expansion varies a
    /// vehicle's identity and numbers and never its make, body style, title or province, so the
    /// seed's distinct values are the catalogue's (SyntheticVehicleSourceTests holds that), and four
    /// DISTINCTs over two hundred rows cost nothing where four over a hundred thousand are four
    /// scans of the table on every warm-up.
    /// </summary>
    public async Task<InventoryFacets> FacetsAsync()
    {
        await using var db = await contexts.CreateDbContextAsync();
        var seed = db.Vehicles.AsNoTracking();
        return new InventoryFacets(
            Sorted(await seed.Select(row => row.Make).Distinct().ToListAsync()),
            Sorted(await seed.Select(row => row.BodyStyle).Distinct().ToListAsync()),
            Sorted(await seed.Select(row => row.TitleStatus).Distinct().ToListAsync()),
            Sorted(await seed.Select(row => row.Province).Distinct().ToListAsync()));
    }

    private static List<string> Sorted(List<string> values)
    {
        values.Sort(StringComparer.Ordinal);
        return values;
    }
}
