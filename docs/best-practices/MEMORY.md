# Memory, held by the build

TheYard is a used-vehicle auction platform built in the open: a .NET 10 API, a React front end, and two databases underneath it. Four apps share one small App Service plan, a B2 with 3.5 GB of memory between them, so a megabyte one app keeps is a megabyte the other three cannot have. This page is the eight rules the apps are held to, each with why it matters, the number this site moved when it applied it, and the code or test that holds it.

## In plain words

A program's memory is a shared room. Anything it keeps "just in case" takes space from everything else on the same machine, and a collection that only ever grows takes all of it eventually. The rules here say what a .NET app on a small shared machine should keep, for how long, and how to make the build notice when one slips.

What that is worth: the site stopped holding a second copy of data its databases already keep, and every rule below is checked on every build instead of remembered.

## The request path, before and after

![A listing request before and after: process memory in the middle, then the store in the middle](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/memory-request-path.svg)

Before, each site loaded all hundred thousand vehicles into its own memory, for both databases, and answered every search by walking that list. The databases held the 200 starting vehicles. After, the databases hold all hundred thousand and answer the search themselves, and the site asks for one page and keeps nothing but a five-second cache (ADR: The store does the work).

Measured before the change, on 1.0.3.176: the plan at 82 per cent of its 3.5 GB, each .NET site about 750 MB of working set and 420 to 460 MB of heap, each holding two hundred thousand vehicles. After, on 1.0.3.179 at 04:43 UTC on 19 October, ten minutes after both fills had finished: the plan at 68 per cent, each .NET site about 360 to 380 MB of working set and 80 to 110 MB of heap under a 986 MB limit, holding no vehicles. The plan did not stay there: it read 73 per cent at 11:30 UTC the same morning, with each .NET site at 366 to 396 MB, and the settled reading a day after 1.0.3.181 rolled is in the same record. The whole table, with the listing times on each store, is in ADR: The store does the work, the addenda of 19 October.

## The eight rules

### a. A store is not a cache

Why: data that already lives in a database and is also held whole in memory is paid for twice, and the memory copy has none of the database's guarantees: no bound, no expiry, rebuilt on every start.

Here: the catalogue was held whole in each process, about 750 MB a site. Now the store holds it and the process holds none once the store answers, which is a number the Admin tab shows and a test reads.

```live path=api/TheYard.Tests/MemoryRulesTests.cs region=rule-a
```

### b. Every cache is bounded, and every cache expires

Why: a dictionary that only grows is a memory leak on a delay. Bound it by a count, by bytes or both, and give every entry an expiry.

Here: one cache type, `BoundedCache`, used for every cache a request fills. The reset-mail limit forgets stale addresses and stops at 10,000. A miss loads once a key, so a cache whose entry has just expired costs the store one query rather than one per visitor who asked in the same moment. A test names every collection field in the six production projects with what bounds it, and fails on a new one until somebody writes its bound down.

```csharp
// Bounded by count and bytes, each entry expiring: the shape every cache here takes.
var pages = new BoundedCache<string, IReadOnlyList<CatalogueEntry>>(
    maxCount: 64, maxBytes: 16L << 20, expiry: TimeSpan.FromSeconds(5),
    bytesOf: page => 64 + page.Count * 1_500);
```

```live path=api/TheYard.Application/InventoryService.cs region=listing-cache
```

### c. Page, never materialise

Why: `ToList()` over a table brings every row into memory to keep a page of them. Put the filter, the order and the page in the query (`Where`, `OrderBy`, `Skip`, `Take`) so the database reads only as far as the page, and read with `AsNoTracking` when nothing is written back, so EF Core keeps no change-tracking copy of each row.

```csharp
// The database orders by an index and stops at the page's last row.
var page = await db.Catalogue.AsNoTracking()
    .Where(row => row.Make == make)
    .OrderBy(row => row.Price).ThenBy(row => row.Seq)
    .Skip(offset).Take(limit)
    .ToListAsync();
```

```live path=api/TheYard.Infrastructure/EfVehicleCatalogue.cs region=ef-catalogue-query
```

### d. Streams over byte arrays past 85 KB

Why: an object of 85,000 bytes or more goes on the large object heap, which is collected only with the oldest generation and is not compacted by default, so a stream of large allocations fragments memory and makes the expensive collections run more often.

Here: reading a picture's size used to load the whole file, up to 941 KB, on every page that asked. It reads the header now: 24 bytes for a PNG, a seek to the frame marker for a JPEG, the first 32,768 characters of an SVG.

```live path=api/TheYard.Api/DocImages.cs region=header-only
```

### e. Workstation GC, concurrent, and a heap limit

Why: Server GC gives each core its own heap and is tuned for a machine of one's own; on a small shared plan, Workstation GC with concurrent collection keeps the footprint small. A heap limit makes a regression the runtime's problem inside its own share rather than the neighbours'.

Here: Workstation and concurrent since ADR-094; the limit is `DOTNET_GCHeapHardLimitPercent=0x1E` on each .NET site. Settings read from the environment are hexadecimal, so `0x1E` is thirty per cent, and a plain `30` would have been forty-eight.

```live path=api/TheYard.Tests/MemoryRulesTests.cs region=rule-e
```

### f. Pool what is expensive

Why: a new `HttpClient` per call opens a new connection per call and can run a machine out of sockets; one per class is a pool per class. Microsoft's guideline for .NET 5 and later is one long-lived handler with `PooledConnectionLifetime` set, or `IHttpClientFactory` where clients are built by dependency injection. One `CosmosClient` per process, for the same reason.

Here: seven readers held seven static clients. They share one handler now, which retires each connection after two minutes so a changed DNS answer is picked up.

```live path=api/TheYard.Api/OutboundHttp.cs region=outbound-http
```

### g. No static collection that grows with traffic

Why: a static field lives as long as the process, so a static list that every request adds to is a leak that no collection can fix. Logs and recent errors are ring buffers of a fixed size.

Here: none was found; the request, log, error and SQL rings were already rings. The collection test above holds that.

### h. Every app measured the same way

Why: a rule nobody measures is a guess. The Node rendering service ran under `--max-old-space-size=192`; whether that was tight or padded is a reading, not an opinion. Read on 19 October it used 18 MB of heap with 79 MB resident, so it runs under `--max-old-space-size=128` now, with room still to spare. `--optimize_for_size` was tried beside it the same night and is a flag Node refuses in `NODE_OPTIONS`: the container did not start until it was taken out, and a test now holds it out, and each .NET site runs with `DOTNET_GCConserveMemory=7`, which has the collector compact the large object heap once it is fragmented.

Here: the rendering service's `/readyz` answers its heap used against its limit, and each .NET site's `/api/admin/machines` answers the collector's own reading.

```live path=api/TheYard.Api/Machines.cs region=gc-reading
```

## Reading the collector's own numbers

`GET /api/admin/machines` answers, for the process that served it, the GC mode in effect, the heap against the limit it may grow to, generation 2 and the large object heap after the last collection, how much is pinned or fragmented, the share of time spent in collections, and how many vehicles the process holds. The two numbers worth watching first are the heap against the limit and the catalogue held, which reads zero.

The card Memory against traffic, under Cloud infrastructure, draws the working set and the processor against requests a minute and reads red when memory climbs while traffic is flat, the shape a leak or an unbounded cache makes; on the readings the site kept it reads red the week the catalogue moved into each process (ADR: Memory follows traffic).

## Further reading

- Microsoft Learn, [Fundamentals of garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals)
- Microsoft Learn, [The large object heap on Windows](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
- Microsoft Learn, [Runtime configuration options for garbage collection](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector)
- Microsoft Learn, [Guidelines for using HttpClient](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- Microsoft Learn, [Tracking vs. no-tracking queries](https://learn.microsoft.com/en-us/ef/core/querying/tracking)
- ADR: The store does the work, the record behind this page
