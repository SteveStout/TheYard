using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The memory rules of docs/best-practices/MEMORY.md, held where a test can hold them
/// (ADR: The store does the work): the process holds no catalogue once the store answers, every
/// collection a long-lived object keeps is named here with what bounds it, every cache is bounded
/// and expires, every outbound call shares one pool, and each .NET site has a heap limit.
/// </summary>
public sealed class MemoryRulesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // #region rule-a
    /// <summary>Rule a. The test host's relational store answers its catalogue, so the count it holds in memory reads zero.</summary>
    [Fact]
    public async Task Once_the_store_answers_the_process_holds_no_catalogue()
    {
        var client = factory.CreateClient();
        // On the relational store by name, so the suite booted on Cosmos DB asks the same question.
        client.DefaultRequestHeaders.Add(Backends.HeaderName, "sql");
        await client.GetAsync("/api/vehicles?limit=1");

        using var machines = JsonDocument.Parse(await client.GetStringAsync("/api/admin/machines"));
        var catalogues = machines.RootElement.GetProperty("container").GetProperty("catalogues").EnumerateArray().ToList();
        var relational = catalogues.Single(c => c.GetProperty("store").GetString()!.Contains("SQL", StringComparison.OrdinalIgnoreCase)).GetProperty("held");

        Assert.Equal("store", relational.GetProperty("mode").GetString());
        Assert.Equal(0, relational.GetProperty("held_in_memory").GetInt32());
        Assert.Equal(TestCatalogue.Vehicles, relational.GetProperty("filled").GetInt32());
        Assert.True(machines.RootElement.GetProperty("container").GetProperty("gc").GetProperty("heap_size_mb").GetDouble() > 0);
    }
    // #endregion rule-a

    // #region rule-b
    /// <summary>
    /// Every collection field in the six production projects, by type and field, with what keeps it
    /// from growing with traffic. A new one fails here until somebody writes its bound down; one
    /// that is gone fails here until its line goes, so the list can only shrink to the truth.
    /// Rows, documents and the records that cross the wire are values, built and dropped per
    /// request, and are left out by their names.
    /// </summary>
    private static readonly Dictionary<string, string> Bounded = new(StringComparer.Ordinal)
    {
        ["TheYard.Application.BoundedCache`2._map"] = "the bounded cache itself: a count and a byte limit, and an expiry",
        ["TheYard.Application.BoundedCache`2._recency"] = "the same cache's order of use, the same size as its map",
        ["TheYard.Application.BoundedCache`2._inflight"] = "the loads under way, one a key, each removed the moment it finishes",
        ["TheYard.Application.MemoryCatalogue._entries"] = "the list catalogue: the tests, the file fallback and a fill's bridge, let go when the store answers",
        ["TheYard.Application.MemoryCatalogue._slots"] = "one per vehicle of the list catalogue above",
        ["TheYard.Application.MemoryCatalogue._texts"] = "one per vehicle of the list catalogue above",
        ["TheYard.Application.MemoryCatalogue._byId"] = "one per vehicle of the list catalogue above",
        ["TheYard.Application.MemoryCatalogue._orders"] = "one sorted order per CatalogueOrder value, eight",
        ["TheYard.Application.MarketService._bids"] = "one per vehicle the room has raised: at most the catalogue, emptied by a reset",
        ["TheYard.Application.BidService._standing"] = "one per vehicle anybody bid on, the store's standing cached: at most the catalogue",
        ["TheYard.Application.BidService._byUser"] = "one per account with a bid, each holding that account's bids",
        ["TheYard.Application.MemoryResourceHistory._minutes"] = "a month of minutes, trimmed on every append",
        ["TheYard.Application.MemoryResourceHistory._hours"] = "a month of hours, trimmed on every append",
        ["TheYard.Application.MemoryPlanHistory._minutes"] = "a month of minutes, trimmed on every append",
        ["TheYard.Application.MemoryPlanHistory._hours"] = "a month of hours, trimmed on every append",
        ["TheYard.Domain.VehicleSearchIndex._byId"] = "one per vehicle of the catalogue it was built over; the site builds none",
        ["TheYard.Domain.ScheduleOrder._starts"] = "one per vehicle of the catalogue it was built over; the site builds none",
        ["TheYard.Domain.ScheduleOrder._ends"] = "one per vehicle of the catalogue it was built over; the site builds none",
        ["TheYard.Domain.ScheduleOrder._byEnd"] = "one per vehicle of the catalogue it was built over; the site builds none",
        ["TheYard.Domain.ScheduleOrder._byStart"] = "one per vehicle of the catalogue it was built over; the site builds none",
        ["TheYard.Api.LogRingBuffer._entries"] = "a ring of RingSizes.LogRing",
        ["TheYard.Api.RequestRingBuffer._entries"] = "a ring of RingSizes.RequestRing",
        ["TheYard.Api.SqlRingBuffer._entries"] = "a ring of two hundred statements",
        ["TheYard.Api.StoreRingBuffer._entries"] = "a ring of two hundred operations",
        ["TheYard.Api.ErrorRingBuffer._entries"] = "a ring of RingSizes.ErrorRing",
        ["TheYard.Api.MachineSampler._samples"] = "a ring of an hour of samples",
        ["TheYard.Api.StartupTimings._steps"] = "the startup's named steps, a handful",
        ["TheYard.Api.RegistrationLimit._taken"] = "the registrations inside the allowance's window, at most the allowance",
        ["TheYard.Api.ForgotLimit._last"] = "at most ForgotLimit.MaxAddresses, the stale ones forgotten first",
        ["TheYard.Api.SelfTestCooldown._last"] = "the accounts that ran the self-test in the last minute",
        ["TheYard.Api.ProofRunner._accounts"] = "the proof's own accounts, one per store, two",
        ["TheYard.Api.ActivityReportCache._entries"] = "one per named window, three",
        ["TheYard.Api.ActivityReportCache._building"] = "one per named window, three",
        ["TheYard.Api.CloudRecorder._hourRead"] = "one per resource card, six",
        ["TheYard.Api.CloudReader._cached"] = "one per card and window, six cards by four windows",
        ["TheYard.Api.CloudReader._month"] = "one per card, six",
        ["TheYard.Api.PlanHistoryReader._cached"] = "one per window, four",
        ["TheYard.Api.ActivityCollector._stores"] = "one per store, two",
        ["TheYard.Api.AdminKey._key"] = "the operator key's bytes, fixed at startup",
        ["TheYard.Api.VisitorTokens._key"] = "the visitor-token key's bytes, fixed at startup",
        ["TheYard.Api.Backends._all"] = "the two backends",
        ["TheYard.Api.SiteHosts._sites"] = "the configured sites, one per public name, read once at startup",
        ["TheYard.Api.RequestRecorder._siteHosts"] = "the same configured sites' names, copied once at startup",
        ["TheYard.Api.CostHistoryReader._cached"] = "one per named window, four",
        ["TheYard.Api.MachineHistoryReader._cached"] = "one per named window, four",
        ["TheYard.Api.KeptRingReader._cache"] = "one per kept card and named window, each a known name",
        ["TheYard.Application.InventoryService._pending"] = "the expansion handed to a fill, taken by the fill the moment it starts",
        ["TheYard.Application.MemoryBidStore._bids"] = "the bid store of a site whose store is down: one row per buyer's bid, gone with the process",
        ["TheYard.Application.MemoryBidStore._standings"] = "the same store's standings, one per vehicle bid on",
        ["TheYard.Application.MemoryResetLinks._kept"] = "reset links, each expiring in an hour and swept on every mint",
        ["TheYard.Infrastructure.Cosmos.CosmosVehicleCatalogue._makeBySeedId"] = "the seed's two hundred ids and their makes",
        ["TheYard.Infrastructure.Cosmos.CosmosStore._physicalPartitions"] = "one per container, read at startup",
        ["TheYard.Infrastructure.Cosmos.CosmosVehicleCatalogue._keysets"] = "one per order, seven, each the key of every 250th row: 400 places",
        ["TheYard.Infrastructure.Cosmos.CosmosVehicleCatalogue._walks"] = "one per order, seven",
    };

    private static readonly string[] Projects =
        ["TheYard.Data", "TheYard.Domain", "TheYard.Application", "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "TheYard.Api"];

    private static bool IsCollection(Type type) =>
        type != typeof(string)
        && (type.IsArray
            || (typeof(IEnumerable).IsAssignableFrom(type) && type.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true));

    private static bool IsValue(Type type) =>
        type.Name.EndsWith("Row", StringComparison.Ordinal)
        || type.Name.EndsWith("Document", StringComparison.Ordinal)
        || type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
        || type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null
        || type.Namespace == "TheYard.Data";

    /// <summary>
    /// A type this codebase wrote. The coverage run instruments the same assemblies and adds its own
    /// tracker classes to them, each with a static hit array, so the scan reads only TheYard's namespaces.
    /// </summary>
    private static bool IsOurs(Type type) => type.Namespace?.StartsWith("TheYard", StringComparison.Ordinal) == true;

    [Fact]
    public void The_scan_reads_only_the_types_this_codebase_wrote()
    {
        Assert.True(IsOurs(typeof(PlanChildren)));
        Assert.True(IsOurs(typeof(MemoryRulesTests)));
        // CI's tracker is Coverlet.Core.Instrumentation.Tracker.<assembly>_<guid>; any other namespace stands in for it.
        Assert.False(IsOurs(typeof(System.Text.StringBuilder)));
    }

    [Fact]
    public void Every_collection_a_long_lived_object_keeps_is_named_with_its_bound()
    {
        var found = Projects
            .SelectMany(name => Assembly.Load(new AssemblyName(name)).GetTypes())
            .Where(type => type.IsClass && IsOurs(type) && !IsValue(type) && !(type.FullName ?? "").Contains('<', StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsLiteral && !field.Name.Contains('<', StringComparison.Ordinal) && IsCollection(field.FieldType) && !IsFrozen(field))
                .Select(field => $"{type.FullName}.{field.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var unnamed = found.Except(Bounded.Keys).Order().ToList();
        var gone = Bounded.Keys.Except(found).Order().ToList();
        Assert.True(unnamed.Count == 0, "collections with no bound written down: " + string.Join(", ", unnamed));
        Assert.True(gone.Count == 0, "bounds for collections that are gone: " + string.Join(", ", gone));
    }

    /// <summary>A static readonly table written once and never added to is a constant, whatever its type.</summary>
    private static bool IsFrozen(FieldInfo field) =>
        field.IsStatic && field.IsInitOnly
        && (field.FieldType.Name.StartsWith("IReadOnly", StringComparison.Ordinal)
            || field.FieldType.Name.StartsWith("Frozen", StringComparison.Ordinal)
            || field.FieldType.IsArray
            || field.FieldType == typeof(HashSet<string>));

    [Fact]
    public void A_bounded_cache_keeps_its_count_its_bytes_and_its_expiry()
    {
        var clock = new StepClock(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        var cache = new BoundedCache<string, string>(3, 100, TimeSpan.FromSeconds(5), text => text.Length, clock, StringComparer.Ordinal);

        cache.Set("a", new string('a', 10));
        cache.Set("b", new string('b', 10));
        cache.Set("c", new string('c', 10));
        Assert.True(cache.TryGet("a", out _));
        cache.Set("d", new string('d', 10));
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet("b", out _));

        cache.Set("big", new string('x', 90));
        Assert.True(cache.Bytes <= 100);
        Assert.True(cache.TryGet("big", out _));
        cache.Set("too-big", new string('y', 101));
        Assert.False(cache.TryGet("too-big", out _));

        clock.Step(TimeSpan.FromSeconds(6));
        Assert.False(cache.TryGet("big", out _));
        cache.Clear();
        Assert.Equal((0, 0L), (cache.Count, cache.Bytes));
    }

    [Fact]
    public async Task A_bounded_cache_asks_the_factory_once_while_the_answer_is_fresh()
    {
        var cache = new BoundedCache<int, int>(10, 1_000, TimeSpan.FromMinutes(1), _ => 4);
        int asked = 0;

        int first = await cache.GetOrAddAsync(1, () => Task.FromResult(++asked));
        int second = await cache.GetOrAddAsync(1, () => Task.FromResult(++asked));

        Assert.Equal((1, 1, 1), (first, second, asked));
    }

    [Fact]
    public async Task A_bounded_cache_loads_a_key_once_for_everybody_who_misses_together()
    {
        var cache = new BoundedCache<int, int>(10, 1_000, TimeSpan.FromMinutes(1), _ => 4);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int asked = 0;

        var waiting = Enumerable.Range(0, 8).Select(_ => cache.GetOrAddAsync(1, () => { Interlocked.Increment(ref asked); return gate.Task; })).ToList();
        gate.SetResult(42);
        int[] answers = await Task.WhenAll(waiting);

        Assert.Equal(1, asked);
        Assert.All(answers, answer => Assert.Equal(42, answer));
    }

    [Fact]
    public async Task A_bounded_cache_keeps_no_failed_load_and_the_next_caller_tries_again()
    {
        var cache = new BoundedCache<int, int>(10, 1_000, TimeSpan.FromMinutes(1), _ => 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync(1, () => Task.FromException<int>(new InvalidOperationException("the store is away"))));
        int answer = await cache.GetOrAddAsync(1, () => Task.FromResult(7));

        Assert.Equal(7, answer);
    }

    [Fact]
    public void The_reset_limit_forgets_stale_addresses_before_it_refuses_a_new_one()
    {
        var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var limit = new ForgotLimit(TimeSpan.FromMinutes(5), () => now);
        for (int i = 0; i < ForgotLimit.MaxAddresses; i++)
        {
            Assert.True(limit.TryTake($"buyer{i}@example.com"));
        }

        Assert.False(limit.TryTake("one-more@example.com"));
        Assert.False(limit.TryTake("buyer1@example.com"));
        now = now.AddMinutes(6);
        Assert.True(limit.TryTake("one-more@example.com"));
        Assert.True(limit.Count <= ForgotLimit.MaxAddresses);
    }
    // #endregion rule-b

    // #region rule-f
    /// <summary>
    /// Rule f. An outbound call goes through OutboundHttp's one pool. The four that build a client
    /// of their own are named, each with its reason; a fifth fails here.
    /// </summary>
    private static readonly Dictionary<string, string> OwnClients = new(StringComparer.Ordinal)
    {
        ["Shared/Hosted/KeepWarmAtRoll.cs"] = "the keep-warm loop's loopback client, cookies off, one for the life of the process",
        ["Shared/AdminRegistration.cs"] = "the proof's and the page sweep's loopback clients, cookies off, one per run",
        ["HourlyReadings.cs"] = "a handler of its own to read the certificate the site answers with, once an hour",
        ["OutboundHttp.cs"] = "the pool itself",
    };

    [Fact]
    public void Every_outbound_call_shares_one_pool_and_the_exceptions_are_named()
    {
        string api = Path.Combine(Repo.Root(), "api", "TheYard.Api");
        var building = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"new HttpClient\s*[({]|disposeHandler:"))
            .Select(path => Path.GetRelativePath(api, path).Replace('\\', '/'))
            .Order()
            .ToList();

        Assert.Equal(OwnClients.Keys.Order(), building);
    }
    // #endregion rule-f

    // #region rule-e
    /// <summary>
    /// Rule e. Both .NET sites carry a heap limit of thirty per cent, written in the hexadecimal the
    /// runtime reads GC settings from the environment in, and keep their catalogues in the store.
    /// </summary>
    [Fact]
    public void Each_site_has_a_heap_limit_of_thirty_per_cent_and_keeps_its_catalogue_in_the_store()
    {
        string template = File.ReadAllText(Path.Combine(Repo.Root(), "infra", "appservice.bicep"));

        Assert.Contains("{ name: 'DOTNET_GCHeapHardLimitPercent', value: '0x1E' }", template, StringComparison.Ordinal);
        Assert.Equal(30, Convert.ToInt32("1E", 16));
        Assert.Contains("{ name: 'Catalogue__Keep', value: 'true' }", template, StringComparison.Ordinal);
        Assert.Contains("{ name: 'DOTNET_GCConserveMemory', value: '7' }", template, StringComparison.Ordinal);
        string render = File.ReadAllText(Path.Combine(Repo.Root(), "infra", "render.bicep"));
        Assert.Contains("{ name: 'NODE_OPTIONS', value: '--max-old-space-size=128' }", render, StringComparison.Ordinal);
        // A V8 flag Node does not allow in NODE_OPTIONS stops the process before it starts: this one took the
        // rendering service down for twenty minutes on 19 October.
        Assert.DoesNotMatch(@"value: .[^']*optimize_for_size", render);
    }
    // #endregion rule-e

    private sealed class StepClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Step(TimeSpan by) => _now += by;
    }
}
