// The catalogue port over the catalogue container: a filter and an order written as one Cosmos DB
// SQL query each, over indexed paths only, a page deep in an order read from a kept place. Its own file
// because this is one of the two query implementations ADR: The store does the work fences, and
// the other is its EF Core twin.
using System.Text;
using Microsoft.Azure.Cosmos;
using TheYard.Application;
using TheYard.Domain;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// <see cref="IVehicleCatalogue"/> on Cosmos DB. Every query keeps <c>c.seq &lt; @target</c>,
/// so the one container answers a test's thousand and the site's hundred thousand, and
/// <c>c.price &gt;= 0</c>, which only a document the fill wrote carries. A read by id is a point
/// read: the synthetic id is a seed id and six characters, and the seed's two hundred makes,
/// read once, give every id its partition, so no vehicle read fans out (ADR: The partition key).
/// A listing without a make fans out to the container's one physical partition, which ADR: The
/// partition key measured at the same cost as a pinned page at this size.
/// </summary>
/// <param name="store">The document store.</param>
/// <param name="target">How many vehicles the catalogue is.</param>
public sealed partial class CosmosVehicleCatalogue(CosmosStore store, int target) : IVehicleCatalogue
{
    private const string Written = "c.seq < @target AND c.price >= 0";

    private Container Catalogue => store.ContainerNamed(Containers.Catalogue);

    private IReadOnlyDictionary<string, string>? _makeBySeedId;

    /// <inheritdoc />
    public int Target => target;

    /// <summary>
    /// A batch of thirty documents a second beside the site, about four hundred request units a
    /// second at the measured nine to fourteen a write, so the fill takes under half of the
    /// account's thousand and visitors keep the rest; up to two thousand missing fill at warm-up.
    /// </summary>
    public CatalogueFillPace Pace { get; } = new(30, TimeSpan.FromSeconds(1), 2_000);

    /// <inheritdoc />
    public async Task<int> FilledAsync()
    {
        var counts = await store.QueryAsync<int>(
            Catalogue,
            new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE {Written}").WithParameter("@target", target),
            partitionKey: null,
            "cross-partition");
        return counts.Count == 0 ? 0 : counts[0];
    }

    /// <summary>Upserts the batch, one transactional batch per make.</summary>
    public async Task FillAsync(IReadOnlyList<CatalogueEntry> batch)
    {
        foreach (var partition in batch.Select(entry => entry.ToCatalogueDocument()).GroupBy(document => document.Make))
        {
            await store.UpsertBatchAsync(Catalogue, partition.Key, [.. partition], "pinned to the make");
        }
    }

    // #region cosmos-catalogue-query
    /// <summary>
    /// The filter as a WHERE clause on indexed paths: the store's own spellings compared with
    /// equality, the segment as a comparison of the slot with now, every typed word as the start of
    /// a word of the searchable text or a status word that names the segment, and the moved ids left out
    /// with one array parameter.
    /// </summary>
    private static (string Where, List<(string Name, object Value)> Parameters, string? Partition) Where(CatalogueFilter filter, int target)
    {
        var where = new StringBuilder(Written);
        var parameters = new List<(string, object)> { ("@target", target), ("@d", filter.SinceAnchorMs) };
        void Equal(string path, string name, string? value)
        {
            if (value is not null)
            {
                where.Append($" AND c.{path} = {name}");
                parameters.Add((name, value));
            }
        }
        Equal("make", "@make", filter.Make);
        Equal("body_style", "@body", filter.BodyStyle);
        Equal("title_status", "@title", filter.TitleStatus);
        Equal("province", "@province", filter.Province);
        where.Append(Segment(filter.Segment) is { } segment ? $" AND {segment}" : "");
        if (filter.MinCondition is { } grade)
        {
            where.Append(" AND c.condition_grade >= @grade");
            parameters.Add(("@grade", grade));
        }
        if (filter.PriceMin is { } low)
        {
            where.Append(" AND c.price >= @low");
            parameters.Add(("@low", low));
        }
        if (filter.PriceMax is { } high)
        {
            where.Append(" AND c.price <= @high");
            parameters.Add(("@high", high));
        }
        for (int i = 0; i < filter.Tokens.Count; i++)
        {
            var token = filter.Tokens[i];
            // The one word rule (VehicleSearchIndex.Holds): three characters or more start a word,
            // which is the text's start or just after a space; a shorter token is anywhere.
            bool word = token.Text.Length >= VehicleSearchIndex.WordLength;
            var either = word
                ? new List<string> { $"STARTSWITH(c.search, @t{i})", $"CONTAINS(c.search, @s{i})" }
                : new List<string> { $"CONTAINS(c.search, @t{i})" };
            either.AddRange(token.AlsoMatches.Select(Segment).OfType<string>());
            where.Append($" AND ({string.Join(" OR ", either)})");
            parameters.Add(($"@t{i}", token.Text));
            if (word)
            {
                parameters.Add(($"@s{i}", " " + token.Text));
            }
        }
        if (filter.Exclude.Count > 0)
        {
            where.Append(" AND NOT ARRAY_CONTAINS(@moved, c.id)");
            parameters.Add(("@moved", filter.Exclude.ToArray()));
        }
        return (where.ToString(), parameters, filter.Make);
    }

    private static string? Segment(CatalogueSegment segment) => segment switch
    {
        CatalogueSegment.Live => "(c.start_offset <= @d AND c.end_offset > @d)",
        CatalogueSegment.Upcoming => "c.start_offset > @d",
        CatalogueSegment.Ended => "c.end_offset <= @d",
        _ => null,
    };

    /// <summary>The order as an ORDER BY one composite index serves (infra/cosmos/catalogue.json), the place breaking every tie.</summary>
    private static string OrderBy(CatalogueOrder order) => order switch
    {
        CatalogueOrder.EndAscending => "c.end_offset ASC, c.seq ASC",
        CatalogueOrder.StartAscending => "c.start_offset ASC, c.seq ASC",
        CatalogueOrder.EndDescending => "c.end_offset DESC, c.seq ASC",
        CatalogueOrder.PriceAscending => "c.price ASC, c.seq ASC",
        CatalogueOrder.PriceDescending => "c.price DESC, c.seq ASC",
        CatalogueOrder.ConditionDescending => "c.condition_grade DESC, c.seq ASC",
        CatalogueOrder.BidsDescending => "c.bid_count DESC, c.seq ASC",
        _ => "c.seq ASC",
    };

    private static QueryDefinition Define(string text, List<(string Name, object Value)> parameters)
    {
        var query = new QueryDefinition(text);
        foreach (var (name, value) in parameters)
        {
            query = query.WithParameter(name, value);
        }
        return query;
    }

    /// <summary>
    /// The three segments' counts: one COUNT per segment the filter keeps, each answered off the
    /// index without loading a document, because the store has no conditional count.
    /// </summary>
    public async Task<SegmentCounts> CountAsync(CatalogueFilter filter)
    {
        async Task<int> Count(CatalogueSegment segment)
        {
            if (filter.Segment != CatalogueSegment.Any && filter.Segment != segment)
            {
                return 0;
            }
            var (where, parameters, partition) = Where(filter.In(segment), target);
            var counts = await store.QueryAsync<int>(Catalogue, Define($"SELECT VALUE COUNT(1) FROM c WHERE {where}", parameters), partition, Label(partition));
            return counts.Count == 0 ? 0 : counts[0];
        }
        return new SegmentCounts(
            await Count(CatalogueSegment.Live),
            await Count(CatalogueSegment.Upcoming),
            await Count(CatalogueSegment.Ended));
    }

    /// <summary>
    /// One page: from the kept places when the page is deep in an order they serve
    /// (CosmosVehicleCatalogue.Keyset.cs), otherwise the ORDER BY, then OFFSET and LIMIT in the
    /// store. The numbers written into the text are integers this code computed.
    /// </summary>
    public async Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        if (take <= 0)
        {
            return [];
        }
        if (await KeysetPageAsync(filter, order, skip, take) is { } kept)
        {
            return kept;
        }
        var (where, parameters, partition) = Where(filter, target);
        var documents = await store.QueryAsync<CatalogueDocument>(
            Catalogue,
            Define($"SELECT * FROM c WHERE {where} ORDER BY {OrderBy(order)} OFFSET {skip} LIMIT {take}", parameters),
            partition,
            Label(partition));
        return [.. documents.Select(document => document.ToEntry())];
    }
    // #endregion cosmos-catalogue-query

    private static string Label(string? partition) => partition is null ? "cross-partition" : "pinned to the make";

    /// <summary>The vehicles with these ids, by point reads, each partition named from the seed's makes.</summary>
    public async Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        var makes = await MakesAsync();
        var items = new List<(string Id, string PartitionKey)>();
        foreach (string id in ids.Distinct(StringComparer.Ordinal))
        {
            if (makes.TryGetValue(id, out var make) || (id.Length > 6 && makes.TryGetValue(id[..^6], out make)))
            {
                items.Add((id, make));
            }
        }
        var documents = await store.ReadManyAsync<CatalogueDocument>(Catalogue, items, "pinned to the make");
        return [.. documents.Where(document => document.Seq < target && document.Price >= 0).Select(document => document.ToEntry()).OrderBy(entry => entry.Seq)];
    }

    /// <summary>
    /// The four lists of distinct values, read from the 200-document seed container: the expansion
    /// varies identity and numbers and never these four fields (SyntheticVehicleSourceTests holds
    /// that), and a DISTINCT over the hundred thousand would load every document to answer it.
    /// </summary>
    public async Task<InventoryFacets> FacetsAsync()
    {
        var seed = await store.QueryAsync<VehicleDocument>(
            store.Vehicles, new QueryDefinition("SELECT * FROM c"), partitionKey: null, "cross-partition");
        _makeBySeedId = seed.ToDictionary(document => document.Id, document => document.Make, StringComparer.Ordinal);
        return new InventoryFacets(
            Distinct(seed.Select(document => document.Make)),
            Distinct(seed.Select(document => document.BodyStyle)),
            Distinct(seed.Select(document => document.TitleStatus)),
            Distinct(seed.Select(document => document.Province)));
    }

    private static List<string> Distinct(IEnumerable<string> values) => [.. values.Distinct().Order(StringComparer.Ordinal)];

    private async Task<IReadOnlyDictionary<string, string>> MakesAsync()
    {
        if (_makeBySeedId is null)
        {
            await FacetsAsync();
        }
        return _makeBySeedId!;
    }
}
