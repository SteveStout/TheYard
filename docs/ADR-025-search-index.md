# ADR: The search index

Status: accepted, 2026-09-03, shipped as 1.0.0.35. The README listed this as
open work: "precompute each vehicle's lowercase haystack at startup instead of
rebuilding it per request."

## Context

Free-text search is a full scan. Every one of the hundred thousand rows is
tested against the query, and the version before this one built the thing it
tested against inside that loop:

```
string haystack =
    ($"{vehicle.Year} {vehicle.Make} {vehicle.Model} {vehicle.Trim} " +
     $"{vehicle.BodyStyle} {vehicle.TitleStatus} {vehicle.Province} {vehicle.City} " +
     $"{AuctionSchedule.StatusFor(vehicle.Id, clock)}")
    .ToLowerInvariant();
return Query
    .ToLowerInvariant()
    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
    .All(haystack.Contains);
```

Three separate pieces of waste, and none of them depends on the row being
tested at that moment:

- The nine-field interpolation and its lowercase copy: two allocations per row,
  a hundred thousand times, for text that has not changed since startup.
- `AuctionSchedule.StatusFor`: an FNV-1a hash and date arithmetic per row, run
  whether or not the query has anything to do with auction status.
- `Query.ToLowerInvariant().Split(...)`: the user typed the query once, and it
  was lowercased and split a hundred thousand times.

## Decision

**Each vehicle's searchable text is built once, when the dataset loads.**
`VehicleSearchIndex` holds it, keyed by vehicle id and built beside the by-id
dictionary that was already built there.

**The query's tokens are computed once per request.** `VehicleFilter.Compile`
returns a predicate with the tokens already lowercased and split. `Matches` is
still there for a caller holding one vehicle; a scan compiles once and reuses.

**The auction status stays out of the index.** It is the one searchable value
the clock decides rather than the data, so it cannot be precomputed. It is
computed only for a token the static text did not already satisfy, which for
most queries is never. This gives the same answer the single concatenated
string gave, because tokens are split on whitespace and so can never straddle
the space between the two parts.

**Keyed by id, not by reference.** The bid overlay rebuilds each vehicle with
`with` before filtering, so the instance the predicate sees is never the
instance the index was built from. Ids survive that; references do not.

## What it bought, measured

The suite carries the measurement, so it is reproducible rather than
remembered: `SearchIndexBenchmarkTests` scans the same hundred thousand rows
with both predicates, no HTTP, no sorting and no serialisation in the way.
Median of five scans, three separate runs, Release build:

| | text rebuilt per row | index |
| --- | --- | --- |
| run 1 | 45 ms | 21 ms |
| run 2 | 37 ms | 14 ms |
| run 3 | 36 ms | 17 ms |

The scan itself is roughly halved. A whole request improves by about the same
absolute amount and a smaller share: `GET /api/vehicles?q=ford&limit=24`
against the local API went from a 138 ms median to 111 ms, because what
remains is dominated by ordering nine thousand matches and serialising a page
of twenty-four. That is the honest shape of this change. It removes two
hundred thousand allocations from a text query and about twenty milliseconds
of work; it does not make the endpoint twice as fast, and the record would be
worth less if it claimed otherwise.

The test asserts only that the indexed path is not slower. A tight timing
assertion on a shared build agent fails for reasons unrelated to the code, and
a suite people learn to re-run is worse than no suite.

## In the code

The index, and the text it holds (`api/TheBlock.Domain/VehicleSearchIndex.cs`):

```live path=api/TheBlock.Domain/VehicleSearchIndex.cs region=text
```

```live path=api/TheBlock.Domain/VehicleSearchIndex.cs region=lookup
```

Compiling the filter once (`api/TheBlock.Domain/VehicleFilter.cs`):

```live path=api/TheBlock.Domain/VehicleFilter.cs region=compile
```

The scan, and why the status is checked separately:

```live path=api/TheBlock.Domain/VehicleFilter.cs region=query
```

Where the two meet (`api/TheBlock.Application/InventoryService.cs`):

```live path=api/TheBlock.Application/InventoryService.cs region=search
```

## Consequences

- Startup does slightly more work: one lowercase string per vehicle, held for
  the process's life. At a hundred thousand rows that is a few megabytes,
  traded for the allocations above. On a container with 1.5 GB it is not close
  to a concern; on a dataset ten times larger it would be worth revisiting,
  and the honest answer at that size is a real inverted index rather than a
  bigger string per row.
- A query whose tokens are all auction statuses ("live", "ended") still pays
  for the hash and the date math per row, because it must. That case did not
  get faster and the numbers above show it.
- The index is built once and never invalidated, which is correct only because
  the dataset never changes after load. A system where vehicles are edited
  would need the index to change with them, and that is a different design:
  this one is right for a read-only dataset and would be wrong for a mutable
  one.
- `VehicleFilter.Matches` is now `Compile(clock)(vehicle)`. A caller in a loop
  that keeps using it gets the old cost, so the scan is the one place that
  matters and the one place that was changed.

## Files

- [`api/TheBlock.Domain/VehicleSearchIndex.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Domain/VehicleSearchIndex.cs): the index, its text and its fallback.
- [`api/TheBlock.Domain/VehicleFilter.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Domain/VehicleFilter.cs): `Compile`, and the two-part token check.
- [`api/TheBlock.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Application/InventoryService.cs): built with the dataset, used by `Search`.
- [`api/TheBlock.Tests/VehicleSearchIndexTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/VehicleSearchIndexTests.cs): the indexed and unindexed paths must answer identically.
- [`api/TheBlock.Tests/SearchIndexBenchmarkTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/SearchIndexBenchmarkTests.cs): the measurement above, and the coverage assertion.
