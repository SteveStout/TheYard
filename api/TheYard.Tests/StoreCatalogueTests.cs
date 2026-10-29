using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TheYard.Data;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// Holds the catalogue the store answers (ADR: The store does the work): the slot written beside
/// every vehicle is the window the hash gives, on any day; the expansion's filter values are the
/// seed's; and the relational adapter's queries answer exactly what the in-memory catalogue answers,
/// count for count and row for row, for every filter, every order and every page tried.
/// </summary>
public sealed class StoreCatalogueTests
{
    private static async Task<IReadOnlyList<Vehicle>> ExpansionAsync(int count) =>
        await new SyntheticVehicleSource(new JsonFileVehicleSource(Path.Combine(Repo.Root(), "data", "vehicles.json")), count).LoadAsync(CancellationToken.None);

    // #region slot
    /// <summary>
    /// The hash stays the authority. For all hundred thousand vehicles and on days either side of
    /// both daylight-saving changes, the anchor plus the stored slot is the window the schedule
    /// derives, to the millisecond, and the segment it puts the vehicle in is the status it derives.
    /// </summary>
    [Fact]
    public async Task The_slot_beside_every_vehicle_is_the_window_the_hash_gives_on_any_day()
    {
        var vehicles = await ExpansionAsync(100_000);
        long[] anchors =
        [
            new DateTimeOffset(2026, 3, 8, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            new DateTimeOffset(2027, 2, 28, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        ];
        int wrong = 0;
        foreach (var vehicle in vehicles)
        {
            var slot = CatalogueSlot.For(vehicle.Id);
            foreach (long anchor in anchors)
            {
                var window = AuctionSchedule.Window(vehicle.Id, anchor);
                long now = anchor + 13 * 3_600_000L;
                var status = AuctionSchedule.Status(window, now);
                var segment = slot.SegmentAt(now - anchor);
                bool same = window.StartsAtMs == anchor + slot.StartOffsetMs
                    && window.EndsAtMs == anchor + slot.EndOffsetMs
                    && segment.ToString() == status.ToString();
                wrong += same ? 0 : 1;
            }
        }

        Assert.Equal(0, wrong);
    }
    // #endregion slot

    /// <summary>
    /// The expansion varies a vehicle's identity and numbers and never its make, body style, title
    /// or province, which is what lets both stores read the filters' values from the 200-row seed
    /// instead of a DISTINCT over a hundred thousand.
    /// </summary>
    [Fact]
    public async Task The_expansions_filter_values_are_the_seeds()
    {
        var seed = new MemoryCatalogue(await new JsonFileVehicleSource(Path.Combine(Repo.Root(), "data", "vehicles.json")).LoadAsync(CancellationToken.None));
        var whole = new MemoryCatalogue(await ExpansionAsync(100_000));

        var a = await seed.FacetsAsync(CancellationToken.None);
        var b = await whole.FacetsAsync(CancellationToken.None);

        Assert.Equal(a.Makes, b.Makes);
        Assert.Equal(a.BodyStyles, b.BodyStyles);
        Assert.Equal(a.TitleStatuses, b.TitleStatuses);
        Assert.Equal(a.Provinces, b.Provinces);
    }

    // #region relational-same-answer
    /// <summary>
    /// The relational adapter over a SQLite file the test owns, filled through the port, against
    /// the in-memory catalogue over the same vehicles: the same three counts and the same rows in
    /// the same order for every filter and order below, at three instants of the day.
    /// </summary>
    [Fact]
    public async Task The_relational_store_answers_every_filter_and_order_as_the_list_does()
    {
        const int Target = 1_200;
        var vehicles = await ExpansionAsync(Target);
        string file = Path.Combine(Path.GetTempPath(), $"theyard-catalogue-{Guid.NewGuid():N}.db");
        try
        {
            var connection = new YardConnection(YardProvider.Sqlite, $"Data Source={file};Pooling=False");
            await using (var db = new YardDbContext(connection.Options()))
            {
                await db.Database.MigrateAsync();
                // The 200-row seed table, which the facets are read from.
                db.Vehicles.AddRange(vehicles.Take(200).Select((v, i) => v.ToRow(i)));
                await db.SaveChangesAsync();
            }
            var store = new EfVehicleCatalogue(new ContextFactory(connection.Options()), Target, local: true);
            var memory = new MemoryCatalogue(vehicles);
            for (int start = 0; start < Target; start += 500)
            {
                await store.FillAsync([.. vehicles.Skip(start).Take(500).Select((v, i) => new CatalogueEntry(start + i, v))], CancellationToken.None);
            }
            // A second pass over the same rows writes nothing twice.
            await store.FillAsync([.. vehicles.Take(10).Select((v, i) => new CatalogueEntry(i, v))], CancellationToken.None);
            Assert.Equal(Target, await store.FilledAsync(CancellationToken.None));

            var facets = await memory.FacetsAsync(CancellationToken.None);
            string make = facets.Makes[1];
            string province = facets.Provinces[0];
            string moved = vehicles[7].Id;
            string longMake = vehicles.First(v => v.Make.Length >= 5).Make.ToLowerInvariant();
            var filters = new List<CatalogueFilter>
            {
                new(),
                new() { Make = make },
                new() { Province = province, MinCondition = 3.5 },
                new() { PriceMin = 10_000, PriceMax = 30_000 },
                new() { Tokens = [new CatalogueToken(vehicles[3].Model.ToLowerInvariant(), [])] },
                new() { Tokens = [new CatalogueToken("live", [CatalogueSegment.Live])] },
                new() { Tokens = [new CatalogueToken("e", [CatalogueSegment.Upcoming, CatalogueSegment.Ended]), new CatalogueToken("20", [])] },
                // A word of three characters or more is the start of a word, from the words table;
                // the same three letters inside a word find nothing there and nothing in the list.
                new() { Tokens = [new CatalogueToken(longMake[..3], [])] },
                new() { Tokens = [new CatalogueToken(longMake[1..4], [])] },
                new() { Tokens = [new CatalogueToken(vehicles[5].City.ToLowerInvariant().Split(' ')[0], []), new CatalogueToken(vehicles[5].Year.ToString(System.Globalization.CultureInfo.InvariantCulture), [])] },
                new() { Segment = CatalogueSegment.Live, BodyStyle = vehicles[0].BodyStyle },
                new() { Segment = CatalogueSegment.Ended, Exclude = [moved, vehicles[11].Id, "not-in-the-catalogue"] },
                new() { Segment = CatalogueSegment.Upcoming, TitleStatus = vehicles[0].TitleStatus },
            };
            long[] instants = [-6 * 3_600_000L, 13 * 3_600_000L, 40 * 3_600_000L];
            int compared = 0;
            foreach (long since in instants)
            {
                foreach (var shape in filters)
                {
                    var filter = shape with { SinceAnchorMs = since };
                    Assert.Equal(await memory.CountAsync(filter, CancellationToken.None), await store.CountAsync(filter, CancellationToken.None));
                    foreach (var order in Enum.GetValues<CatalogueOrder>())
                    {
                        foreach (var (skip, take) in new[] { (0, 24), (30, 7), (1_150, 100) })
                        {
                            var expected = (await memory.PageAsync(filter, order, skip, take, CancellationToken.None)).Select(e => (e.Seq, e.Vehicle.Id)).ToList();
                            var actual = (await store.PageAsync(filter, order, skip, take, CancellationToken.None)).Select(e => (e.Seq, e.Vehicle.Id)).ToList();
                            Assert.True(expected.SequenceEqual(actual), $"{order} {skip}/{take} at {since} for {filter.Key()}");
                            compared++;
                        }
                    }
                }
            }

            var found = await store.FindAsync([moved, vehicles[900].Id, "nope"], CancellationToken.None);
            Assert.Equal([7, 900], found.Select(entry => entry.Seq));
            Assert.Equivalent(vehicles[7], found[0].Vehicle, strict: true);
            Assert.Equal(facets.Makes, (await store.FacetsAsync(CancellationToken.None)).Makes);
            Assert.True(compared > 600);

            // A site whose catalogue is smaller than what the file holds answers as its own
            // catalogue: once it has counted the store and found rows past its target, every query
            // starts from the place again, which the full catalogue above leaves out.
            const int Smaller = 1_000;
            var narrower = new EfVehicleCatalogue(new ContextFactory(connection.Options()), Smaller, local: true);
            Assert.Equal(Smaller, await narrower.FilledAsync(CancellationToken.None));
            var first = new MemoryCatalogue([.. vehicles.Take(Smaller)]);
            var now = new CatalogueFilter { SinceAnchorMs = 13 * 3_600_000L };
            Assert.Equal(await first.CountAsync(now, CancellationToken.None), await narrower.CountAsync(now, CancellationToken.None));
            Assert.Equal(
                (await first.PageAsync(now, CatalogueOrder.PriceDescending, 0, 50, CancellationToken.None)).Select(e => e.Seq),
                (await narrower.PageAsync(now, CatalogueOrder.PriceDescending, 0, 50, CancellationToken.None)).Select(e => e.Seq));
        }
        finally
        {
            File.Delete(file);
        }
    }
    // #endregion relational-same-answer

    // #region relational-plans
    /// <summary>
    /// The relational adapter's statements are read the way the database plans them. Every count
    /// reads an index alone (a count that reads the table took 400 ms to a minute on the PC under
    /// the browser suite), and no page of an order the indexes serve sorts its matches, because a
    /// page that sorts every match reads every match (ADR: The store does the work).
    /// </summary>
    [Fact]
    public async Task The_relational_store_counts_off_an_index_alone_and_pages_without_sorting()
    {
        const int Target = 2_000;
        var vehicles = await ExpansionAsync(Target);
        string file = Path.Combine(Path.GetTempPath(), $"theyard-plans-{Guid.NewGuid():N}.db");
        try
        {
            var connection = new YardConnection(YardProvider.Sqlite, $"Data Source={file};Pooling=False");
            await using (var db = new YardDbContext(connection.Options()))
            {
                await db.Database.MigrateAsync();
            }
            var capture = new Statements();
            var options = new DbContextOptionsBuilder<YardDbContext>();
            connection.Configure(options);
            options.AddInterceptors(capture);
            var store = new EfVehicleCatalogue(new ContextFactory(options.Options), Target, local: true);
            await store.FillAsync([.. vehicles.Select((v, i) => new CatalogueEntry(i, v))], CancellationToken.None);
            Assert.Equal(Target, await store.FilledAsync(CancellationToken.None));

            long since = 13 * 3_600_000L;
            string make = vehicles[0].Make;
            CatalogueFilter[] counted =
            [
                new() { SinceAnchorMs = since },
                new() { SinceAnchorMs = since, Make = make },
                new() { SinceAnchorMs = since, Make = make, Segment = CatalogueSegment.Upcoming },
                new() { SinceAnchorMs = since, Segment = CatalogueSegment.Ended },
                new() { SinceAnchorMs = since, PriceMin = 10_000, PriceMax = 30_000 },
                new() { SinceAnchorMs = since, MinCondition = 4 },
                new() { SinceAnchorMs = since, Tokens = [new CatalogueToken(vehicles[3].Model.ToLowerInvariant(), [])] },
                new() { SinceAnchorMs = since, Tokens = [new CatalogueToken("live", [CatalogueSegment.Live])] },
            ];
            foreach (var filter in counted)
            {
                capture.Clear();
                await store.CountAsync(filter, CancellationToken.None);
                string plan = await PlanAsync(file, Assert.Single(capture.Taken));
                Assert.True(plan.Contains("COVERING INDEX", StringComparison.Ordinal), $"the count for {filter.Key()} reads the table: {plan}");
            }

            (CatalogueFilter Filter, CatalogueOrder Order)[] paged =
            [
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Live }, CatalogueOrder.EndAscending),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Upcoming }, CatalogueOrder.StartAscending),
                (new() { SinceAnchorMs = since, Make = make, Segment = CatalogueSegment.Live }, CatalogueOrder.EndAscending),
                (new() { SinceAnchorMs = since }, CatalogueOrder.PriceAscending),
                (new() { SinceAnchorMs = since }, CatalogueOrder.PriceDescending),
                (new() { SinceAnchorMs = since }, CatalogueOrder.ConditionDescending),
                (new() { SinceAnchorMs = since }, CatalogueOrder.BidsDescending),
                (new() { SinceAnchorMs = since }, CatalogueOrder.Seq),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Live }, CatalogueOrder.BidsDescending),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Live }, CatalogueOrder.ConditionDescending),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Live }, CatalogueOrder.PriceDescending),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Upcoming }, CatalogueOrder.PriceAscending),
                (new() { SinceAnchorMs = since, Segment = CatalogueSegment.Ended }, CatalogueOrder.BidsDescending),
            ];
            foreach (var (filter, order) in paged)
            {
                capture.Clear();
                await store.PageAsync(filter, order, 100, 100, CancellationToken.None);
                string plan = await PlanAsync(file, Assert.Single(capture.Taken));
                Assert.False(plan.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.Ordinal), $"the {order} page for {filter.Key()} sorts every match: {plan}");
            }

            // A page deep in the order reads its places off the order's own index alone, so the rows
            // it skips over are index entries and never the table's.
            foreach (var order in new[] { CatalogueOrder.EndAscending, CatalogueOrder.PriceAscending, CatalogueOrder.BidsDescending })
            {
                capture.Clear();
                var live = new CatalogueFilter { SinceAnchorMs = since, Segment = CatalogueSegment.Live };
                await store.PageAsync(live, order, EfVehicleCatalogue.DeepSkip, 100, CancellationToken.None);
                string plan = await PlanAsync(file, capture.Taken[0]);
                Assert.True(plan.Contains("COVERING INDEX", StringComparison.Ordinal), $"the deep {order} page reads the table for the rows it skips: {plan}");
            }

            // A typed word is a range on the words table's word index, which hands back the places;
            // the page's places are read off them first, and only the page's own rows come from the
            // table after (ADR: The store does the work, the addendum of 19 October).
            var word = new CatalogueToken(vehicles.First(v => v.Make.Length >= 5).Make.ToLowerInvariant()[..3], []);
            foreach (var order in Enum.GetValues<CatalogueOrder>())
            {
                foreach (var segment in new[] { CatalogueSegment.Live, CatalogueSegment.Upcoming, CatalogueSegment.Ended })
                {
                    capture.Clear();
                    var filter = new CatalogueFilter { SinceAnchorMs = since, Segment = segment, Tokens = [word] };
                    await store.PageAsync(filter, order, 0, 25, CancellationToken.None);
                    string plan = await PlanAsync(file, capture.Taken[0]);
                    Assert.True(plan.Contains("COVERING INDEX IX_CatalogueWords_Word (Word>? AND Word<?)", StringComparison.Ordinal), $"the {order} page of {segment} for a word does not seek the word index: {plan}");
                    Assert.False(plan.Contains("IX_Catalogue_Words", StringComparison.Ordinal), $"the {order} page of {segment} for a word scans every row's text: {plan}");
                }
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// The document store reads a deep page from kept places only where the places count the
    /// rows: the whole catalogue in any order, the live segment by end, the upcoming by start and
    /// the ended by end, each in the order the default listing reads it. Any other filter is a set
    /// the places do not count, and keeps OFFSET (ADR: The store does the work, the addendum of 19 October).
    /// </summary>
    [Fact]
    public void The_document_store_keeps_places_only_for_the_orders_whose_rows_they_count()
    {
        var whole = new CatalogueFilter { SinceAnchorMs = 1 };
        Assert.True(CosmosCatalogueKeysets.KeysetServes(whole, CatalogueOrder.PriceAscending));
        Assert.True(CosmosCatalogueKeysets.KeysetServes(whole with { Segment = CatalogueSegment.Live }, CatalogueOrder.EndAscending));
        Assert.True(CosmosCatalogueKeysets.KeysetServes(whole with { Segment = CatalogueSegment.Upcoming }, CatalogueOrder.StartAscending));
        Assert.True(CosmosCatalogueKeysets.KeysetServes(whole with { Segment = CatalogueSegment.Ended }, CatalogueOrder.EndDescending));
        Assert.False(CosmosCatalogueKeysets.KeysetServes(whole, CatalogueOrder.Seq));
        Assert.False(CosmosCatalogueKeysets.KeysetServes(whole with { Segment = CatalogueSegment.Live }, CatalogueOrder.PriceAscending));
        Assert.False(CosmosCatalogueKeysets.KeysetServes(whole with { Make = "Ford" }, CatalogueOrder.PriceAscending));
        Assert.False(CosmosCatalogueKeysets.KeysetServes(whole with { Tokens = [new CatalogueToken("ford", [])] }, CatalogueOrder.PriceAscending));
        Assert.False(CosmosCatalogueKeysets.KeysetServes(whole with { Exclude = ["a"] }, CatalogueOrder.PriceAscending));
    }

    /// <summary>SQLite's plan for one statement, its steps joined, with the statement's own parameters.</summary>
    private static async Task<string> PlanAsync(string file, (string Text, (string Name, object? Value)[] Parameters) statement)
    {
        await using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + statement.Text;
        foreach (var (name, value) in statement.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        var steps = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            steps.Add(reader.GetString(3));
        }
        return string.Join(" | ", steps);
    }

    /// <summary>The catalogue statements a context sends, with their parameters, for reading their plans.</summary>
    private sealed class Statements : DbCommandInterceptor
    {
        private readonly List<(string Text, (string Name, object? Value)[] Parameters)> _taken = [];

        public IReadOnlyList<(string Text, (string Name, object? Value)[] Parameters)> Taken
        {
            get
            {
                lock (_taken)
                {
                    return [.. _taken];
                }
            }
        }

        public void Clear()
        {
            lock (_taken)
            {
                _taken.Clear();
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"Catalogue\"", StringComparison.Ordinal))
            {
                lock (_taken)
                {
                    _taken.Add((command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, (object?)parameter.Value))]));
                }
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
    // #endregion relational-plans
}
