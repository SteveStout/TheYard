# ADR: The ports learn to wait

Status: accepted, 2026-09-08. The first decision of the Cosmos DB work that
reaches the Application layer: every member of the three ports in `Ports.cs`
now returns a `Task`, `BidService` holds a semaphore where it held a lock, and
the host warms the catalogue and the bids before it serves. Parent: ADR: A
second store on Cosmos DB, and what it costs.

## In plain words

The parts of the code that read and write data (the ports) now wait for the database without holding a thread (every port returns a `Task`). The new store has no way to answer without waiting, and on a one-CPU container a blocked thread stalls every request behind it.

What that is worth: a developer can add another store later without a slow database freezing the site, and the organization gets a site where a slow store slows bidding and nothing else.

## Context

`IVehicleSource.Load()`, `IPhotoManifestSource.Load()` and the three members
of `IBidStore` were synchronous, and they were synchronous for a reason that was
true when they were written: the first store was a pair of JSON files, and
reading a file is a synchronous thing to do. The second store, SQLite and then
Azure SQL Database through EF Core, has a synchronous driver, so the ports kept
their shape and `EfBidStore.Save` called `SaveChanges` on a request thread.

The third store has no synchronous driver. The Cosmos DB SDK is asynchronous
throughout, and the EF Core provider for it throws on `ToList` and
`SaveChanges`. So a Cosmos adapter behind a synchronous port has exactly one way
to exist: call the asynchronous method and block until it finishes.

## Why blocking was not taken

Blocking on a task in ASP.NET Core does not deadlock, because there is no
synchronization context to deadlock on. It is still the wrong answer here, and
the reason is arithmetic about the container rather than a rule about style.

The live container has one vCPU, so the thread pool starts with one worker
thread. A bid that blocks that thread while the store answers, five to ten
milliseconds in region, holds every other request behind it until the pool
notices it is starved and injects another thread, which it does roughly twice a
second. Under a burst of bids the site would stall in half-second steps, and the
stall would be invisible in any measurement taken one request at a time.
`PlaceBid` also held a `lock` across the store call, so the bids behind the
first one would be blocked threads waiting on a blocked thread. On a lane whose
subject is performance, that is the defect the interview would be about.

## Decision

**Every port member returns a `Task`.** `LoadAsync`, `SaveAsync`, `ClearAsync`.
No cancellation tokens, deliberately: a bid write that is halfway through the
store-then-memory sequence must finish, and the two loads run once at startup.

```live path=api/TheYard.Application/Ports.cs region=ports
```

**`BidService` holds a semaphore, one at a time, across the await.** A `lock`
cannot be held across an `await`, and the store is awaited inside the critical
section on purpose: the store is written first and memory second, so a store
that throws leaves nothing behind (ADR: The relational store). Same guarantee,
same shape, one bidder at a time:

```live path=api/TheYard.Application/BidService.Bidding.cs region=place
```

**The constructor stopped reading the store.** A constructor cannot wait, and
the store now has to be waited for. `LoadAsync` replays the store once, whoever
asks first; the host asks at startup and the writing methods ask again, which
costs nothing once the load is done and means a service nobody warmed loads
itself on its first bid:

```live path=api/TheYard.Application/BidService.cs region=store
```

**`InventoryService` warms once and reads a finished task after that.** The
`Lazy` that held the catalogue now holds the task that loads it. `WarmAsync`
starts the load; every synchronous accessor reads the task's result, and after
the host has awaited `WarmAsync` that read is a field access on a task that
finished at startup. A caller that skips the warm-up, which is what a unit test
over an in-memory source does, blocks on a task the memory source has already
completed, a wait of no time. The only way to block a thread here for real is to
skip the warm-up against a store that goes over the network, and the host does
not:

```live path=api/TheYard.Application/InventoryService.cs region=warm
```

The host's side of that bargain is two lines, before the pipeline is built:

```csharp
await app.Services.GetRequiredService<InventoryService>().WarmAsync();
await app.Services.GetRequiredService<BidService>().LoadAsync();
```

**The relational adapters became honest about what they always were.** EF Core
has had `ToListAsync`, `FindAsync`, `SaveChangesAsync` and `ExecuteDeleteAsync`
all along; `EfSources.cs` uses them now, and `YardDatabase.Prepare` and
`YardSeed.EnsureSeeded` are `PrepareAsync` and `EnsureSeededAsync`. The JSON
readers use `File.ReadAllTextAsync`. Nothing about what any of them does changed.

## What the tests hold

The shape, by reflection, so that a synchronous member added next year fails
here rather than on a one-vCPU container:

```live path=api/TheYard.Tests/PortsTests.cs region=port-surface
```

And the gate, with fifty bids at the minimum arriving together through a store
that takes a moment to answer. Exactly one is accepted and the store is written
exactly once; without the gate two bidders read the same price, both pass the
rules, and the lower one lands second:

```live path=api/TheYard.Tests/PortsTests.cs region=gate
```

The rest of the suite followed the ports mechanically: the in-memory fakes in
four test files return `Task.FromResult`, the store tests await their saves, and
the bidding tests await their bids. 325 tests, all green, after the change.

## Consequences

- A request thread is never blocked on a store. The bid endpoint, the reset
  endpoint and the two startup loads are asynchronous end to end.
- The critical section in `BidService` now includes the store's answer, which
  it always did in practice; the difference is that the thread waiting its turn
  is not a thread.
- `IBidStore.SaveAsync` is called inside the gate, so a slow store slows
  bidding rather than the site, which is the right thing for it to slow.
- Every adapter, present and future, has to be asynchronous. That is the point.
- `RunChecks` on the health endpoint is still synchronous; the Cosmos probe it
  gains is the one place a health check awaits a store, and the health record
  says how.

## Where it sits

This decision starts in Application, where every member of `IVehicleSource`, `IPhotoManifestSource` and `IBidStore` in `Ports.cs` now returns a `Task` and `BidService` guards its writes with a `SemaphoreSlim`, then spreads outward to the async adapters in Infrastructure and to the host, which awaits `WarmAsync` and `LoadAsync` from `Composition/Startup.cs` before serving. The ports had been shaped by the first store, a file read that never waits; a network store behind them would have blocked the only thread on a one-vCPU container. The change cost every adapter, fake and test a mechanical rewrite, and the shared warm-up task later needed fixing so a failed load could be retried. A store that only ever read local files on a many-core host would not have needed this, though any network store makes it the right shape.

## Files

- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the three seams, awaitable.
- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the semaphore, and the load that moved out of the constructor.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the warm-up.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs): the relational adapters, asynchronous.
- [`api/TheYard.Infrastructure/JsonFileSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/JsonFileSources.cs): the file readers, the same.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the two awaits before the pipeline.
- [`api/TheYard.Api/Endpoints/BidEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/BidEndpoints.cs): the bid endpoints.
- [`api/TheYard.Tests/PortsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PortsTests.cs): the shape and the gate.

## Addendum, 2026-09-09: a failed warm is tried again

The `Lazy<Task>` that shared one load between every caller kept a faulted
task as faithfully as a finished one, so a store that was unreachable for one
second at startup would have answered every request until the next roll with
that second's exception, while the host's log said the store's first visitor
would try again (a review with no context read both and put them side by
side). `InventoryService.WarmAsync` and `BidService.LoadAsync` now keep the
task themselves: a load in flight or finished is shared as before, and a load
that failed is replaced by the next caller's, under a lock so the start stays
single. Replaying the bid store twice is safe because `Record` keeps the
higher standing. Two tests hold it, one per service, each with a source that
is down for its first call and up for its second. The live blocks above show
the code as it is.

## Addendum, 2026-09-09: the one path the claim did not cover

"A request thread is never blocked on a store" was true of the host this
record describes, which warmed its one store before it served. Since one
container runs both stores (ADR: One container, both stores) there is a
second store, warmed in the background on a deployed group and not at all in
a test application, and a request that reached it before the warm finished
read `InventoryService`'s synchronous accessor and blocked its thread on the
load, for the seconds after a roll on the live site and on every first
request to the other store under test. A review with no context read the
accessor and this record together and asked. The pipeline now awaits the
request's store's warm before any endpoint runs, which after the first time
is an await on a task that finished at startup; the accessor is unchanged and
is reached only warm (ADR: Three readers with no memory of the project).

```live path=api/TheYard.Api/Stores.cs region=warm-before-reading
```

```live path=api/TheYard.Tests/WarmthTests.cs region=warm-before-reading
```

## Addendum, 2026-10-07 (1.0.3.89): the server listens first

The host no longer loads the default store's catalogue before it listens. Measured on the three rolls App Insights held in full (1.0.3.84, 1.0.3.85 and 1.0.3.87), App Service stopped the old container 1 to 3.5 minutes before the new one was listening, because the new one loaded a hundred thousand vehicles first, on the one core both sites share, and for that stretch nothing answered at all. Steve's call on 7 October: answer first, load after.

So the warm runs beside the server. The page's own files and the platform's probes, `/healthz` and `/readyz`, answer within seconds of the container starting; every API read waits for the load, and waits without holding a thread, because the pipeline already awaits the request's store before any `/api` endpoint runs (the addendum above). The bids replay before the catalogue loads, not after: they are a few rows and done in well under the catalogue's time, so a read let through by the warm finds them in place, and nobody sees a sold vehicle offered or their own bids missing after a roll. Readiness now includes the catalogue, so `/readyz` says 503 until it has loaded, and the deploy's Verify step polls it the way it polls the version. One promise of this record is kept in a different shape: a dataset that cannot be loaded still ends the process, but by logging the failure and stopping the host after it has started, rather than by never starting it.

```live path=api/TheYard.Api/Composition/Startup.cs region=listen-first
```


## Addendum, 2026-10-07 (1.0.3.91): what listening first changed, measured

The addendum above blamed the dark stretch on the catalogue loading before the server listened. The roll of 1.0.3.89 says that was most of the wrong answer. A probe on Steve's machine read each site's origin and domain every two seconds through the roll (`greenlane-probe-10389.log`): the old containers stopped answering at 14:51:09 CDT, and nothing answered, `/healthz` included, until 14:54:12, about three minutes. `/healthz` answers the instant the process listens and reads no store, so those three minutes are App Service replacing the container on its one instance: stopping the old one, then pulling and starting the new one. Both domains answered 504 after 28 seconds, repeatedly, through that stretch.

What listening first did change is everything after it. The new version's API answered within seconds of `/healthz` returning, and the slowest read after that was 2 to 7 seconds with one first read of 29 seconds, against worst reads of 18 to 112 seconds in the first 150 seconds of the three rolls Application Insights held in full. The dark stretch is the platform's, and no change inside the container removes it; two containers overlapping does, which on this plan needs memory it does not have: the plan reads 92 per cent used at the median over ten days.
