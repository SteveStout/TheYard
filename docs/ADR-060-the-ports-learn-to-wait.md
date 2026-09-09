# ADR: The ports learn to wait

Status: accepted, 2026-09-08. The first decision of the Cosmos DB work that
reaches the Application layer: every member of the three ports in `Ports.cs`
now returns a `Task`, `BidService` holds a semaphore where it held a lock, and
the host warms the catalogue and the bids before it serves. Parent: ADR: A
second store on Cosmos DB, and what it costs.

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

```live path=api/TheYard.Application/BidService.cs region=place
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

## Files

- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the three seams, awaitable.
- [`api/TheYard.Application/BidService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/BidService.cs): the semaphore, and the load that moved out of the constructor.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): the warm-up.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs): the relational adapters, asynchronous.
- [`api/TheYard.Infrastructure/JsonFileSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/JsonFileSources.cs): the file readers, the same.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the two awaits before the pipeline, and the bid endpoints.
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