# ADR: The store does the work

Status: accepted, 2026-10-18. The hundred thousand vehicles live in the store and nowhere else: the Catalogue table on Azure SQL Database and the catalogue container on Cosmos DB, each vehicle written once with the slot its id's hash gives its auction window. A listing, the filter values, a vehicle by id and the simulated room's picks are queries against the store the request is on; the store filters, orders and pages, and only the page comes back. The deterministic expansion of the 200 seed vehicles is now the seeder: it fills an empty store once and is let go. Each .NET site runs under a heap limit of 30 per cent.

## Reading list

For Steve, when he is fresh, in this order:

1. [Memory, held by the build](https://theyard.stevenstout.biz/?doc=memory), the Best Practices page: the eight rules, each with why, the number this change moved, and the code that holds it.
2. This record: why the catalogue in memory was a cache pretending to be a store, what replaced it, and what it measured.
3. [ADR-094, compiled before it ships](https://theyard.stevenstout.biz/?doc=adr-compiled-before-it-ships): where the GC settings this record leans on were chosen.
4. Microsoft Learn, [Fundamentals of garbage collection](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/fundamentals): generations, and why a long-lived object costs more than a short-lived one.
5. Microsoft Learn, [The large object heap on Windows](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap): the 85,000-byte line and what crossing it costs.
6. Microsoft Learn, [Runtime configuration options for garbage collection](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector): every setting this record names, including the one that has to be written in hexadecimal.

Each outside link was fetched on 2026-10-18 before it was cited.

## In plain words

The site used to load all hundred thousand vehicles into its own memory, twice, once for each database, and answer every search by sorting through that copy. The databases held only the 200 starting vehicles. Now the databases hold all hundred thousand and answer the search themselves; the site asks for one page and gets one page.

What that is worth: the memory the four apps share stops being spent on a second copy of data a database already keeps, and the database does the job it is built for. A slower answer is the price, and this record says exactly how much slower.

## Context

Steve's ruling, 2026-10-18, 15:41 to 15:43 CDT: keep the B2, keep the hundred thousand vehicles, but stop holding them in RAM, "this work should happen on the database", and hold the apps to memory best practices he can learn from.

The plan card read 2.77 GB of 3.5 GB at 15:40 CDT. Measured before anything changed, 1.0.3.176 at 22:01 to 22:03 UTC, about fifty minutes after its roll:

| reading | SQL site | Cosmos DB site |
| --- | ---: | ---: |
| working set | 753.2 MB | 746.6 MB |
| GC heap | 418.5 MB | 461.2 MB |
| managed bytes in use | 386.3 MB | 341.3 MB |
| catalogues loaded | both, 2 x 100,000 | both, 2 x 100,000 |
| listing p50, server, 32 reads | 28 ms | 43 ms |
| listing p95 | 68 ms | 69 ms |

The plan as a whole: 82 per cent, 2.87 of 3.5 GB, amber, with a thirty-day 95th of 83 and a peak of 93. Workstation GC with concurrent collection was already in effect on both (ADR-094), and `Store:WarmOthers` was already off, but the keep-warm loop loaded the other store's catalogue within minutes anyway, so each site held both.

A catalogue in memory was a cache pretending to be a store. It had no bound, it never expired, it was rebuilt from the seed on every start, and it was the one copy the site trusted. The databases, which are the place a hundred thousand rows belong, held two hundred.

## Decision

**The store holds the hundred thousand, each with its slot.** `AuctionSchedule.Window` puts every window at the day's anchor plus an offset that depends on the id alone. Those two offsets, start and end, are written beside each vehicle at the fill, with the price a buyer competes against and the lowercased searchable text. The hash stays the authority. A test reads all hundred thousand slots back and holds each one to the window the hash gives on four anchors either side of both daylight-saving changes, and the status the clock derives to the segment the slot gives. A vehicle's status is still derived from the window and the clock; the store compares the slot with now instead of the code doing it.

```live path=api/TheYard.Application/CatalogueFilter.cs region=slot
```

```live path=api/TheYard.Tests/StoreCatalogueTests.cs region=slot
```

**Application plans, the adapters translate.** The port `IVehicleCatalogue` has five primitives: how full the store is, a fill, the counts split by schedule segment, one page in one order, and point reads by id. The listing is planned once, in `InventoryService`. The default listing is three segments read one after another (live by closest end, upcoming by soonest start, ended by most recent end), each off its own index, stopping at the page's last row. The adapters only translate a filter and an order into their own query:

```live path=api/TheYard.Infrastructure/EfVehicleCatalogue.cs region=ef-catalogue-query
```

```live path=api/TheYard.Infrastructure.Cosmos/CosmosVehicleCatalogue.cs region=cosmos-catalogue-query
```

On SQL Server each order is one index ending in `Seq` (`api/TheYard.Database/Tables/Catalogue.sql`), so a page is a seek and a scan that stops, and every read is `AsNoTracking`. The two schedule indexes carry the other end of the window, so a segment's count and a live page's test of its start never read the row, and a filtered count reads one narrow index holding only the columns a count compares, `IX_Catalogue_Counts`, rather than the table at about two kilobytes a row. That index leads with the columns no page ranges over, so the planner never pages off it and then sorts. Measured on the PC before it was added, against a local file of all hundred thousand: the unfiltered count took 400 to 600 ms and a listing 300 to 1,100 ms, enough for seven of the browser suite's 168 tests to run out of time under four browsers at once. A query starts from `Seq < target` only when the table holds rows past the catalogue, because that range over the clustered key is what kept the planner off the order's own index. When bids have moved prices, the count is the store's cached count less the moved vehicles that match on the dataset's figures, and the page is the store's cached page made longer by that many, with the moved ones dropped in the process and each row's place worked out from how many moved vehicles sort before it. Putting the ids into the store's query made it test every row it read against the list, 25 seconds for a count and 58 for a page once under the browser suite, and gave the cache a new key with every bid. And a cache miss loads once a key: everybody who asks while a count is loading waits for that one. Each descending order has an index that descends on its figure and ascends on the place, the way the page is ordered, because an index the wrong way round on either key had SQLite sort every live vehicle for a page of twenty-five (27 seconds once under the suite). A local SQLite file pools its connections, so its page cache outlives a statement; the pool for that one file is emptied before the file is deleted. `StoreCatalogueTests` reads SQLite's own plan for every count and page shape and fails on a count that reads the table or a page that sorts its matches. Counts are read only when a number needs them: unfiltered, the total is the catalogue's size, so the default listing's first pages, the ones most visitors read, cost the store one page and no count. A typed word is looked for in `IX_Catalogue_Words`, the searchable text beside every column a word's count or page compares or orders by. On a SQLite file, whose planner has no statistics, a word's page reads its places off that index alone, with the schedule and order columns written as `+ 0` so no other index can draw the planner into reading every row of a range for its text, and then reads the page's rows by their place. On Cosmos DB every path a WHERE names is in the indexing policy, every two-key ORDER BY has its composite index, and a vehicle read by id is a point read: the synthetic id is a seed id and six characters, and the seed's two hundred makes give every id its partition, so nothing a visitor opens fans out (ADR: The partition key). An unfiltered listing does fan out, to the container's one physical partition, which that record measured at the cost of a pinned page at this size.

**A bid moves a price the store does not hold.** Everybody's bids and the room's answers stay in memory, as they were (BidService, MarketService). When a move can change which vehicles match or where they sort (a price filter, a price sort, the bid-count sort), the store leaves those vehicles out, they are worked out in memory from their raised figures, and the two ordered lists are merged at the page. Otherwise the store's page is the page and the moves are laid on its rows. A test holds every page, at every offset tried, to the page the full sort over the raised catalogue gives.

```live path=api/TheYard.Application/InventoryService.Search.cs region=overlay
```

**The expansion is the seeder.** At warm-up the site counts what its store holds. Full, the store answers and nothing is loaded. Short by a few thousand, the warm-up fills it first. Short by more, the site answers from the expansion in memory while it fills its own store beside itself at the store's pace, then switches, lets the list go, and runs one compacting collection so the dial shows the memory the same minute. The other site watches and switches when the fill is done. `Catalogue__Keep` turns that on for the deploy and leaves every test host alone.

```live path=api/TheYard.Application/InventoryService.Warm.cs region=choose
```

**Every cache is bounded.** `BoundedCache` holds the listing's counts and pages for one five-second bucket of the clock and the vehicles read by id for ten minutes, each bounded by a count and by an estimate of its bytes.

```live path=api/TheYard.Application/InventoryService.cs region=listing-cache
```

**A heap limit on each site.** `DOTNET_GCHeapHardLimitPercent` is `0x1E` on both .NET sites in `infra/appservice.bicep`. GC settings read from the environment are hexadecimal, so `30` would have been forty-eight per cent. With the limit, a regression is collected harder, and refused if it must be, inside its own share instead of taking memory from the other three apps on the plan.

## The audit

The eight rules of the Memory page, applied to every app on the plan:

| rule | before | after | status |
| --- | --- | --- | --- |
| a. A store is not a cache | the catalogue held whole in each process, both stores | the store holds it; the process holds 0 once the store answers | fixed by this change |
| b. Every cache bounded, every cache expires | the listing had no cache; the reset-mail limit and the self-test cooldown grew with every address and account; the memory keeps' hours grew forever | `BoundedCache` for the listing; both limits forget stale entries and the mail limit stops at 10,000; the hours trimmed to a month; a test names every collection field with its bound | fixed by this change |
| c. Page, never materialise | every listing walked the whole list | OFFSET and FETCH in the store, `AsNoTracking` on every read | fixed by this change |
| d. Streams over byte arrays past 85 KB | a picture's size read by loading the whole file, up to 941 KB; a document rendered on every read | the header only; each rendered document kept once, bounded | fixed by this change; the rendered documents are deliberately not streamed, because the live fences rewrite a document whole |
| e. Workstation GC, concurrent, and a heap limit | workstation and concurrent since ADR-094; no limit | `0x1E` on both sites | fixed by this change; The Shed is a .NET app with its own template and is the next one |
| f. Pool what is expensive | seven readers each with a static `HttpClient`; one `CosmosClient` per process already | one `SocketsHttpHandler` for every outbound reader, connections retired after two minutes | fixed, by the form Microsoft's guidelines name for .NET 5 and later rather than `IHttpClientFactory`; the loopback clients keep handlers of their own, named in the test |
| g. No static collection that grows with traffic | none found; the rings were already rings | the same, now held by the collection test | already met |
| h. The renderer measured the same way | Node heap against `--max-old-space-size=192` | read below, after the roll | measured |

```live path=api/TheYard.Tests/MemoryRulesTests.cs region=rule-b
```

## What it measured

The before is the table under Context. The after is read once the roll has finished, each store's fill has finished and the dial has settled ten minutes, and it is written into the addendum below with the same readings, because a number on this page has to be one somebody read.

## Consequences

- A listing is now a store round trip or three, where it was a walk of a list in the same process, and the store bills for it: request units on Cosmos DB, DTU on SQL Database. The five-second cache is what keeps a busy minute from paying for the same page twice.
- The rule "nothing schedule-related is persisted" now has one exception, the slot, which is a copy of the hash's answer and is held to it by a test. CLAUDE.md says so.
- A site whose store is down still serves, from the expansion in memory, the way it did before this change. That is the one place the old cost comes back, and the Admin tab says when it has.
- `ScheduleOrder` and the search index are no longer used by the site. They stay in Domain, with their tests, because this change was held to leaving Domain as it was; they go with the next change that touches it.

## Where it sits

The port, `IVehicleCatalogue`, and the one plan for every listing, `InventoryService` and its parts, in `TheYard.Application`, beside the list catalogue the tests and the file fallback use. The two translations in the two adapter projects: `EfVehicleCatalogue` and the Catalogue table's row and model in `TheYard.Infrastructure`, `CosmosVehicleCatalogue` and its document in `TheYard.Infrastructure.Cosmos`. The schema in `TheYard.Database`, the container's policy in `infra/cosmos/`. The host wires one catalogue per store in `Composition/Catalogues.cs` and owns the outbound pool and the collector's reading. Domain does not change: the slot is the schedule's own answer on an anchor of zero.

What would change it: a catalogue too big for one physical partition or for the Basic tier's DTU, at which point the Cosmos key takes the make and the segment together and the SQL side takes a columnstore or a larger tier.

## Files

- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs) - `IVehicleCatalogue`, the five primitives
- [`api/TheYard.Application/CatalogueFilter.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/CatalogueFilter.cs) - the filter in the store's terms, the orders, the segments and the slot
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs) and its parts `.Warm.cs`, `.Search.cs`, `.Moved.cs`, `.Find.cs` - the warm-up and the fill, the listing plan and the overlay's merge, the store's answer without the moved vehicles, the reads by id
- [`api/TheYard.Application/MemoryCatalogue.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/MemoryCatalogue.cs) - the list catalogue: the tests, the file fallback and a fill's bridge
- [`api/TheYard.Application/BoundedCache.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BoundedCache.cs) - the one cache shape
- [`api/TheYard.Infrastructure/EfVehicleCatalogue.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfVehicleCatalogue.cs), [`CatalogueRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/CatalogueRows.cs) and [`YardDbContext.Catalogue.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/YardDbContext.Catalogue.cs) - the relational adapter
- [`api/TheYard.Database/Tables/Catalogue.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Catalogue.sql) - the table and one index per order
- [`api/TheYard.Infrastructure.Cosmos/CosmosVehicleCatalogue.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosVehicleCatalogue.cs) and [`Documents.Catalogue.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/Documents.Catalogue.cs) - the document adapter
- [`infra/cosmos/catalogue.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/catalogue.json) - the indexed paths and the composite indexes
- [`api/TheYard.Api/Composition/Catalogues.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/Catalogues.cs) - one catalogue per store, and who fills
- [`api/TheYard.Api/OutboundHttp.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/OutboundHttp.cs) - the one outbound pool
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep) - `Catalogue__Keep` and the heap limit
- [`api/TheYard.Tests/StoreCatalogueTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StoreCatalogueTests.cs) - the slot against the hash, and the relational adapter against the list
- [`api/TheYard.Tests/InventoryServiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/InventoryServiceTests.cs) - the plan, the merge and the switch
- [`api/TheYard.Tests/MemoryRulesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/MemoryRulesTests.cs) - the memory rules a test can hold
- [`docs/best-practices/MEMORY.md`](https://github.com/SteveStout/TheYard/blob/main/docs/best-practices/MEMORY.md) - the eight rules, for a reader new to them

## Addendum, 2026-10-19: what it measured after

Read on 1.0.3.179 at 04:43 UTC, ten minutes after both fills had finished at 04:33, with the same readings the table under Context used, from each site's own `/api/admin/machines`, the plan card and 32 listing reads per site at the offsets a visitor reaches (0 to 3,100), timed by the server:

| reading | SQL site | Cosmos DB site |
| --- | ---: | ---: |
| working set | 753.2 to 360.0 MB | 746.6 to 383.0 MB |
| GC heap | 418.5 to 82.4 MB | 461.2 to 108.2 MB |
| managed bytes in use | 386.3 to 93.7 MB | 341.3 to 85.1 MB |
| catalogues held in memory | 2 x 100,000 to 0 | 2 x 100,000 to 0 |
| heap limit | none to 986.4 MB | none to 986.4 MB |
| listing p50, server | 28 to 19 ms | 43 to 387 ms |
| listing p95, server | 68 to 103 ms | 69 to 4,162 ms |

The plan: 82 per cent before, 68 per cent after, 2.38 of 3.5 GB, below its amber line for the first time since the catalogue moved into the processes on 8 October. The rendering service read about 90 MB and The Shed about 37 MB, as before.

The SQL side is faster than it was from memory, because each order is an index and a page is a seek that stops. The Cosmos DB side is slower, and the record says so rather than averaging it away: its `OFFSET` reads every document it skips, so the request units grow with the offset (16 RU for the first page, 1,665 for an offset of 40,000, which took 8.4 seconds and is more than the account's 1,000 RU a second), and the account is a region away from the plan, the 38 to 40 ms round trip ADR-079 measured, paid several times by a cross-partition ordered query. The p95 above includes three deep reads at 5,000, 20,000 and 40,000 on each site. On Azure SQL Database the same deep reads took 243, 299 and 298 ms once 1.0.3.179 read a deep page's places off the order's index; on 1.0.3.178 an offset of 40,000 took 6.4 seconds. The fix on the Cosmos DB side is a keyset read, the next page asked for after the last row's end and place rather than by a count of rows to skip, and it is the next change to this record.

## Addendum, 2026-10-19 (1.0.3.182): the scan reads only this codebase's types

The rule that every collection a long-lived object keeps is named with its bound scans the six projects' assemblies by reflection. Under CI's coverage run those assemblies also carry the coverage collector's tracker classes, each with a static hit array, and the scan failed on them from 1.0.3.178 to 181 while the gate, which does not collect coverage, passed. `MemoryRulesTests` now reads only types whose namespace is TheYard's, and `The_scan_reads_only_the_types_this_codebase_wrote` holds the filter. No bound changed and no collection was added (ADR: The five-minute gate, the addendum of this date).
