# ADR: Measuring both stores

Status: accepted, 2026-09-08. Both containers, the same image, the same
region, the same 1 CPU and 1.5 GB, measured in one session from one machine,
with the request charge beside every millisecond the document store can put
one on. Parent: ADR: A second store on Cosmos DB, and what it costs. The
partition key's own numbers are in ADR: The partition key.

## The method

Not a stopwatch and not a vibe. This repository learned the lesson twice
(ADR: The search index; ADR: The SQL Server backend, the addendum on the CI
failure that could not explain itself), and the method is the one in
`SearchIndexBenchmarkTests`: paired rounds, both sides inside each round, the
order alternated, and the median of the per-round differences reported beside
each side's own medians, because sequential timings over a shared network
measure the network and pairing measures the difference.

Every round is what a visitor does, in order: register, sign in, load a listing
page, open a vehicle, read the filter values, place a bid, raise it, and reset.
Twenty rounds, each on a fresh account, against each container's own Azure
address rather than the domain, so the edge is not in either column; the edge's
own share is measured once, separately, at the end.

The request charges are not measured from the client, because the client
cannot see them. They are read from the Cosmos DB container's own Admin
metrics after the rounds, which is the container reporting what the store
charged it for each route, as the median over the requests it saw
(ADR: What the store is actually doing).

The cold starts are the ones each container actually had on the roll that
put this version on it, read from each container's own startup timings, which
count from the process's own start time (ADR: Backends, side by side). Nothing
was restarted to make a number.

```live path=scripts/measure_stores.py region=*
```

## The numbers

Measured 2026-09-08 at 16:32 CDT, twenty paired rounds from Steve's machine in
Missouri against both containers' Azure addresses in West US 2, both on
1.0.0.91 and the same image, the same 1 CPU and 1.5 GB. Milliseconds are the
whole request as the client saw it, so about 120 of every number is the
network between Missouri and Washington, and the last column is the one that
cancels it: the median of the per-round differences, Cosmos DB minus SQL.

| what a visitor does | SQL p50 | SQL p95 | Cosmos p50 | Cosmos p95 | median difference | Cosmos RU per request |
| --- | --- | --- | --- | --- | --- | --- |
| register | 318.6 | 378.5 | 215.6 | 227.6 | -105.2 | 13.04 (two point reads that miss, two creates) |
| sign in | 249.3 | 343.2 | 221.1 | 349.7 | -34.9 | 2.00 (two point reads) |
| listing page | 164.6 | 179.7 | 168.0 | 271.9 | +7.5 | 0 |
| vehicle page | 127.3 | 144.5 | 133.1 | 517.4 | +5.2 | 0 |
| filter values | 145.4 | 160.8 | 146.0 | 170.1 | -0.6 | 0 |
| bid write | 218.6 | 223.7 | 135.0 | 147.1 | -77.6 | 6.52 (a point read that misses, a create) |
| bid raise | 218.7 | 227.3 | 134.5 | 158.3 | -79.9 | 11.29 (a point read, a replace) |
| reset | 171.8 | 193.2 | 137.9 | 162.1 | -33.5 | 7.78 (a query inside the partition, a batch of one delete) |

The request units are exact, read out of the Cosmos DB container's own store
log after the rounds and added up per request: every one of the twenty
sign-ins cost 2.00 RU, every registration 13.04, every bid write 6.52. The
operations behind them, with their in-region latencies, from the same log:

| operation, in region | RU | ms |
| --- | --- | --- |
| point read that finds the document | 1.00 | 1 to 3 |
| point read that finds nothing (404) | 1.00 | 1 to 3 |
| create a bid document (about 200 bytes) | 5.52 | 5 to 7 |
| replace a bid document with `If-Match` | 10.29 | 4 |
| create an account document | 5.52 | 5 |
| query for one buyer's bid ids, inside the partition | 2.26 | 3 |
| transactional batch of one delete | 5.52 | 5 |

And the cold starts, each container's own, on the roll that put 1.0.0.91 on it:

| cold start | SQL | Cosmos |
| --- | --- | --- |
| store check: schema present, or containers present | 2,483 ms | 2,003 ms |
| first-boot seed, or the check that it is there | 339 ms | 450 ms (0 RU: already seeded on the previous roll) |
| catalogue load, 200 rows or 200 documents, expanded to 100,000 | 1,654 ms | 1,190 ms |
| bids load | 130 ms | 17 ms |
| process start to ready | 6,117 ms | 4,688 ms |
| the seed itself, measured on the roll before | 200 rows and 50 photos in about a second | 1,380 RU in 2,154 ms |

## Reading them

**The paths that never touch a store are equal, to within noise.** The listing
page, the vehicle page and the filter values differ by seven, five and less
than one millisecond over twenty rounds, on requests of 130 to 170 ms. That is
parity by construction, which is what the design record promised: those
requests are served from the in-memory index on both sides and the store is not
on their path.

**Every path that touches a store is faster on Cosmos DB, by a margin that is
the geography and the driver rather than the engine.** A bid write is 135 ms
against 219, a registration 216 against 319. The Azure SQL Database is in West
US 3 because West US 2 refused to create one (ADR: The SQL Server backend), so
every SQL statement pays a hop between regions that the Cosmos DB account, in
the container's own region, does not. A bid is two operations on both sides, a
read and a write, so the difference is the hop and the driver rather than the
shape of the work. The honest reading is that the second container's store is
next door and the first's is one region away, and the numbers say what that
costs: about 80 ms on a write, about 35 on a sign-in.

**The cold starts are a wash and both are dominated by the same thing.** Six
seconds against four and a half, and in both cases most of it is the store
check, the connection being established and the identity token acquired,
and the catalogue being expanded to 100,000 in memory, which is the same code
on both sides. The Cosmos DB container's check had been 48,793 ms on 1.0.0.90,
before the identity library was pinned (ADR: A second store on Cosmos DB, and
what it costs, addendum), and that number is the reason cold start is measured
rather than assumed.

**The whole session cost the document store 1,096 request units.** Twenty
registrations, twenty sign-ins, forty bids and twenty resets, on the free
tier, for $0.00, and it would have been a twenty-seventh of a cent on
serverless.

## The edge's share

The domain, `theyard.stevenstout.biz`, is the same SQL container behind
Netlify's edge. `/api/facets` took 677 ms through the edge and 149 ms at the
origin in the same minute, which is why the comparison above is at the origins
and why a visitor comparing the two tabs by feel is comparing the edge as much
as the store. A second visit through the edge is faster once its connection is
warm; that number is the first.

## What did not carry over

Everything a visitor does, the same tests on both stacks, and the suite booted
on either store, pass. The differences that remain are the ones the records
name rather than ones a demo finds:

- The document store carries no `images` field, because none is ever shown,
  and a test holds the precondition (ADR: A second store on Cosmos DB, and what
  it costs).
- Uniqueness of an address is a claim document rather than an index, and there
  is no cascade from an account to its bids (ADR: Accounts on a document store).
- The SQL card is the operations card on the document container, with fields
  the SQL card cannot have (ADR: What the store is actually doing).
- The default view's sort, ending soonest, cannot be pushed down to the document
  store at all, because the schedule is derived from the id and the clock rather
  than stored. On the parity path that never arises, since the sort runs in
  memory on both sides; it is written here because the experiment made it
  visible (ADR: The partition key).

## Files

- [`scripts/measure_stores.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/measure_stores.py): the method, runnable.
- [`api/TheYard.Tests/SearchIndexBenchmarkTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SearchIndexBenchmarkTests.cs): where the paired-rounds method came from.
- [`api/TheYard.Api/Peer.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Peer.cs): the route grouping the charges are read through.
- [`docs/ADR-058-the-partition-key.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-058-the-partition-key.md): the experiment's numbers.
