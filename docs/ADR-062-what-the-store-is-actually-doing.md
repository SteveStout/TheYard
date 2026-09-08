# ADR: What the store is actually doing

Status: accepted, 2026-09-08. The document store's counterpart of the SQL card
on the Admin tab: every operation this container sent to Azure Cosmos DB, with
the container, the kind, the partition it was pinned to or the fact that it
fanned out, and the request charge beside the milliseconds. Parent: ADR: A
second store on Cosmos DB, and what it costs. The record this one sits beside,
ADR: What the database is actually doing, has an addendum pointing here.

## Context

The Admin tab's most valuable card for a data-access reviewer is the one that
shows the raw SQL and what each statement cost in time, with nowhere to put a
parameter value (ADR: What the database is actually doing). Cosmos DB has no
SQL log to intercept, and the thing worth showing about it is different: a
statement's cost on Cosmos DB is a number the service returns with every
response, in request units, and whether an operation touched one partition or
every partition is the difference the interview question is about.

## Decision

**A parallel port, not a wider one.** `ISqlLog` and `SqlStatement` are left as
they are. A second port, `IStoreLog`, records a `StoreOperation`:

```live path=api/TheYard.Application/StoreLog.cs region=store-log-port
```

The two record different things. A SQL statement has text, parameters and a
duration. A store operation has a container, a kind, a partition, a request
charge and a duration, and only sometimes any text. One type for both would
carry nulls on every row on both sides, and the Admin card would be reading a
type to find out which half of it to believe.

**The adapter writes the log, not an interceptor.** There is no command to
intercept: the SDK is called directly (ADR: A second store on Cosmos DB, and
what it costs), and every call goes through one wrapper that times it, reads
the charge off the response, and records it whether it succeeded or failed:

```live path=api/TheYard.Infrastructure.Cosmos/CosmosStore.cs region=operations
```

**The no-values rule carries over exactly.** A parameter is a
`SqlParameterShape`, which has no field for a value. And a partition is
described rather than named: "pinned to the buyer", "pinned to the account",
"cross-partition". The key of an account's partition is the account's id, and
the key of a claim document is an email address, so a log that printed the
partition key value would print the thing the SQL card was designed never to
print. A point read is logged as `ReadItem` with a parameter named `id` of type
`String` and the id's length, which is the same amount of information the SQL
card gives about `@normalizedEmail`.

**Every fan-out says how far it fanned.** A query with no partition key is
recorded as cross-partition with the container's physical partition count
beside it, read once at startup from the SDK's feed ranges. At this size that
count is one, and the card says one, because "cross-partition, 1 physical
partition" is the honest description of a fan-out to a single server and the
number will change when the data does (ADR: The partition key).

**A query is one line with its pages added up.** The SDK answers a query a page
at a time and charges per page. The log records one line per query with the
charge of every page summed and the page count in the outcome, so a two-page
load reads as one operation that cost what it cost, rather than two lines a
reader has to add.

**Self-observation is filtered the same way.** The health check's two point
reads every thirty seconds, and the reads this page itself causes, are dropped
before they reach the ring, for the reason the SQL ring drops its own: left in,
the card would show nothing but the act of reading it.

## What it looks like

On the Cosmos container the SQL card is replaced by this one. On the relational
container this endpoint answers an empty list and the SQL card stays. One
image, one page, and the page shows whichever store it is on:

```live path=src/components/AdminPanel.tsx region=store-card
```

The cold start is the first thing in the log: four `ReadContainer` metadata
reads, then the count queries that decide whether to seed, then the two
cross-partition `SELECT * FROM c` loads of the catalogue and the bids, each with
its charge. After that, an idle container records nothing, and a visitor's bid
records a point read and a point write pinned to the buyer, about six request
units between them.

## What the tests hold

The same canary as the SQL card, on both stores: register an address, read what
the store ran, assert the users container is in it and the address is not, in
the operations and in the log lines. `AdminObservabilityTests` asks the
container which store it is on and reads the matching card, so one test holds
the rule against both:

```live path=api/TheYard.Tests/AdminObservabilityTests.cs region=what-the-store-ran
```

And against the real account, the store tests assert that every operation
carries a charge, that the only cross-partition operations are the loads, and
that no operation's text or partition carries the address (ADR: Accounts on a
document store).

## Consequences

- A reviewer can watch the operations this container sends to Cosmos DB, see
  which request caused each one, and see what each cost in request units, on a
  public page, with no login and no portal.
- The comparison card has a number to put beside every millisecond on the
  document side, which is what makes it a comparison rather than two stopwatches.
- One more fixed-size ring in memory, two hundred operations, emptied on every
  roll.
- The relational side did not change. `ISqlLog`, the interceptor and the SQL
  card are as they were, and a container on SQL Server never sees the new type.

## Files

- [`api/TheYard.Application/StoreLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/StoreLog.cs): the type with nowhere to put a value, and the port.
- [`api/TheYard.Infrastructure.Cosmos/CosmosStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosStore.cs): the wrapper every operation goes through.
- [`api/TheYard.Api/AdminObservability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/AdminObservability.cs): the ring and the window's numbers.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the endpoint, and the request charge in the metrics.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card.
- [`api/TheYard.Tests/AdminObservabilityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AdminObservabilityTests.cs): the canary, on both stores.
- [`docs/ADR-043-what-the-database-is-doing.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-043-what-the-database-is-doing.md): the SQL card this one is the sibling of.
