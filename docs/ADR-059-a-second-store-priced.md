# ADR: A second store on Cosmos DB, and what it costs

Status: proposed, 2026-09-08, written before the code so that Steve can read the
design and the arithmetic before anything is built on them. Steve's ask: a second,
complete backend on Cosmos DB, "the full stack", catalogue, bids and accounts,
running the same image in a second container beside the Azure SQL one, so the
two can be opened in two tabs and compared. The bar: "as fast as SQL Server, on
a different data structure", at the cheapest cost that still hits it. And the
personal goal behind it, in his words: "learn cosmos DB and make sure I learn
how to performance tune it".

This record is the parent. The partition key, which is the one decision that
cannot be changed later, has its own record (ADR: The partition key).

## Context: where the database actually is

Before designing for speed it is worth knowing where the database sits in the
request path, because it is not where a reader of a data-layer diagram would
assume.

`InventoryService` loads the catalogue once, through `IVehicleSource.Load()` and
`IPhotoManifestSource.Load()`, and serves every browse, filter, sort and page
from an in-memory index after that (ADR: The search index). The store is on
exactly four paths:

1. cold start, when the catalogue and the bids are read once;
2. `IBidStore.Save`, once per accepted bid;
3. `IBidStore.Clear`, once per reset;
4. Identity, on register, sign in and "who am I".

So browse, filter, sort and page are already store-independent, and parity there
is guaranteed by construction rather than won by tuning. The three places parity
has to be earned are cold start, the bid write, and sign-in and register. Those
three are what gets measured first.

**The store holds 200 vehicles, not 100,000.** This is the fact that changes the
cost arithmetic and it was read out of the code on 2026-09-08 rather than assumed:
`Vehicles` holds the 200-record seed, and `SyntheticVehicleSource` expands it in
memory to 100,000 by deriving ids from the seed (ADR: The SQL Server backend, the
paragraph on the foreign key that would be a lie). A cold start on Cosmos DB is
therefore about 200 documents, and the 1000 RU/s ceiling of the free tier is not
the wall it would be against 100,000.

**What a document weighs**, measured over all 200 seed records as compact JSON:

| | bytes per document | total |
| --- | --- | --- |
| as the seed carries it | 953 to 1,417, median 1,159, mean 1,167 | 233,312 |
| without `images` | median 800, mean 815 | 162,969 |

The `images` field is 30 per cent of every document and none of it is ever shown.
`PhotoGallery.SelectPhotos` picks gallery photos by body style from the manifest,
every one of the five body styles in the seed has a pool, and `InventoryService`
replaces the images for every vehicle whose style has one, which is all of them.
The stored URLs are placeholders that survive from before the photo set existed.
On SQL Server they are a JSON column nobody reads; on Cosmos DB, where every
kilobyte moved is billed, they would be 30 per cent of every write and every read.
So the Cosmos document does not carry them, a test asserts that every body style
in the seed has a pool (which is the precondition that makes the field dead), and
this paragraph is the record saying what changed and why.

## The account, and what it costs

The rates, read from Microsoft's retail price API for West US 2 on 2026-09-08
(queue script 604, the same source ADR: The SQL Server backend priced its database
from):

| meter, westus2 | retail |
| --- | --- |
| provisioned, 100 RU/s | $0.008 per hour ($5.84 per 100 RU/s per month) |
| provisioned, free tier, 100 RU/s | $0.00 per hour |
| serverless, 1M RU | $0.25 |
| data stored | $0.25 per GB per month |
| data stored, free tier | $0.00 |
| Container Instances, standard vCPU | $0.0405 per hour |
| Container Instances, standard memory | $0.00445 per GB per hour |

**The free tier slot was open.** Checked before anything was created, by
`az cosmosdb list` across the subscription (queue script 602), which answered
`[]`: no Cosmos DB account existed, so the one free tier account the subscription
is allowed still had its slot. That decides the tier by the rule set before the
work started: free tier if the slot is open, serverless if it is spent.

**Created, and read back rather than assumed** (queue script 603, 13:03 to 13:07
CDT):

```
name              cosmos-theyard-ss
location          West US 2 (the container's region; SQL is one region away in West US 3)
freeTier          true
consistency       Session
writeLocations    West US 2 only
localAuthDisabled true      (no keys exist; the account answers only Entra tokens)
provisioning      Succeeded (128 seconds)
```

Two data-plane role assignments, both read back: Cosmos DB Built-in Data
Contributor at the account scope for the container's user-assigned identity
`id-theyard-ss`, and the same role for Steve's own signed-in principal so the
tests can run against the real account from the runner. No key was created,
listed or read at any point, and with local auth disabled none can be: this is
the same shape as the SQL server that has no SQL login (ADR: The SQL Server
backend).

**Committed cost: $0.00 per month for the store.** The free tier is 1000 RU/s and
25 GB for the life of the account. One database, `theyard`, holds that throughput
as shared throughput across its containers, because that is the only layout the
free tier pays for whole: a container with its own throughput starts at 400 RU/s,
so four dedicated containers would be 1600 RU/s and 600 of them billed.

**The container is not free, and the record should say so.** The prompt's list
of things this adds was "no DNS, no certificate, no other resource", and that is
true of the network. The second container group itself is compute: 1 vCPU and
1.5 GB in West US 2 is $0.0405 + 1.5 x $0.00445 = $0.047 per hour, about $34 per
month at list price if it runs all month. Today that is drawn from the
subscription's free trial credit (quota `FreeTrial_2014-09-01`, spending limit on,
read back by script 604), which is the same credit the live container runs on.
Container Instances bill per second while the group runs, so the honest way to
keep the comparison at $0.00 is to run the second container for the comparison
sessions and the interview and stop it in between (`az container stop`). That is
a decision for Steve and it is priced here so he can make it.

### The arithmetic, on the parity path

Estimates first, from the measured document sizes and Microsoft's published RU
rules (a point read of a 1 KB item is 1 RU; a write is about five times that with
a minimal index; a query pays for the index lookup plus the documents it loads).
Every number in this table is replaced by a measured one in ADR: Measuring both
stores, and this table is kept so the estimate and the measurement can be read
side by side.

| operation | estimate | at 1000 RU/s |
| --- | --- | --- |
| seed, 200 vehicles + 50 photos, once | about 1,500 to 2,500 RU | two to three seconds |
| cold start: read 250 documents | about 400 to 700 RU | under one second |
| one accepted bid: point read + point write | about 6 to 8 RU | milliseconds |
| one sign-in: two point reads, one write on a failed guess | about 2 to 8 RU | milliseconds |
| one registration: two creates | about 10 to 12 RU | milliseconds |

A generous month, fifty cold starts, a thousand bids and a hundred sign-ins, is
on the order of 50,000 RU. At serverless rates that would be a cent. On the free
tier it is $0.00, and the 1000 RU/s ceiling is never approached on this path.

### The arithmetic, on the 100,000-document experiment

The parity path leaves the interviewer's question about partition keys and
cross-partition queries with no live example behind it, because 200 documents
in memory cannot show one. The experiment is a fifth container, `catalogue`, that
holds the 100,000 expanded vehicles and serves filtered pages from the store,
never from the in-memory index, so the partition key and the indexing policy
are on the hot path where they can be measured.

| | estimate | at 1000 RU/s |
| --- | --- | --- |
| storage, 100,000 x 815 bytes | 82 MB (of 25 GB) | $0.00 |
| seed with the minimal indexing policy | about 6 RU per document, 600,000 RU | about 10 minutes |
| seed with the default indexing policy | about 10 RU per document, 1,000,000 RU | about 17 minutes |
| read the whole container at cold start | about 250,000 RU | about four minutes: never done |

That last row is why the experiment lives in its own container: reading it whole
would fail the parity bar by minutes, so the catalogue the site boots from stays
at 200 documents and the experiment container is only ever queried a page at a
time. The two seed rows are the headline tuning number this lane exists to
produce, and both are measured rather than trusted.

## The shape of the data

One database, `theyard`, 1000 RU/s shared. Five containers, each defined by a
JSON file under `infra/cosmos/` that a person applies with `az cosmosdb sql
container create`, exactly as the SQL schema is a project a person publishes
(ADR: Data first, and the database in source control, and its addendum).

| container | partition key | what it holds | indexing |
| --- | --- | --- | --- |
| `vehicles` | `/make` | the 200 seed vehicles, read whole at cold start | nothing beyond the key |
| `photos` | `/style` | the 50 manifest entries, read whole at cold start | nothing |
| `bids` | `/user_id` | one document per buyer per vehicle, point read and point write | nothing |
| `users` | `/id` | one document per account, plus one claim document per email address | nothing |
| `catalogue` | `/make` | the 100,000 expanded vehicles, for the experiment only | the paths the filters and sorts use |

"Nothing" means the policy excludes every path, which is the single biggest cost
lever on this workload: the default policy indexes every path in every document,
and every index entry is paid for on every write. The four parity containers are
read whole or by key and never queried by a property, so an index would cost the
seed and earn nothing, which is the same reasoning ADR: The relational store gave
for having no indexes on SQL Server. The experiment container is the exception
and the tuning exercise: seed it with the default policy and with the minimal one,
and measure both.

The test containers are the same five with a `tests-` prefix and a time to live of
one day, so a test that dies leaves nothing behind for longer than that. They
share the same 1000 RU/s and cost nothing extra.

The `bids` and `users` shapes are decided in the records for those steps; the
partition keys are decided in ADR: The partition key, with the reasoning per
query.

## Consistency: session

Session, which is the account default and what this application needs. A visitor
who places a bid reads their own write back on the next request from the same
client, which is the guarantee session gives, and the room's competing bids are
served from memory, not the store. Strong and bounded staleness read from a
quorum and cost twice the RU on every read; nothing here is worth paying double
for, and the reads that matter happen once at cold start anyway. The teaching
record walks the five levels.

## Identity: the hard one

ASP.NET Core Identity's relational shape is a user row plus claims, logins,
tokens and roles in separate tables, joined on demand. There are no joins here.
Three options were on the table:

1. **Keep accounts on SQL Server.** The honest answer for a lot of real systems,
   and it would have made this a two-store application with the hard part
   avoided. Steve chose the full stack, and a comparison that skipped accounts
   would not measure sign-in, which is one of the three paths parity has to be
   earned on.
2. **`IdentityDbContext` on the Cosmos provider.** It compiles. Seven entity
   types land in a document store with no joins, no unique index, and a model
   that is still relational in shape; `FindByEmailAsync` becomes a query across
   every partition, and two registrations of the same address a millisecond
   apart both succeed because the unique index that stopped them on SQL Server
   does not exist.
3. **A custom `IUserStore` over one user document.** Chosen. One document per
   account in `users`, partition key `/id`, so "who am I" is a point read. And a
   second, tiny document per address, id `email:<normalized address>`, which is
   how uniqueness is enforced on a store that cannot enforce it across
   partitions: registering creates the claim document first, a second attempt
   at the same address gets a 409 from the store rather than a race, and only
   then is the user document written. Sign-in is two point reads, claim then
   user, and a failed password is one write to move the lockout count. The
   store implements the five interfaces this application uses, password, email,
   lockout, security stamp and the base, and none of the ones it does not.

That third option is where the document model costs something, and the record
says so plainly: uniqueness is a document you write rather than a constraint
you declare, a registration that dies between the two writes leaves a claim
with no account behind it until it is cleaned up, and there is no cascade to
delete a user's bids with the user. Each of those is a line of code on this side
and a line of DDL on the other, and the comparison record keeps the list.

## The SDK, not the EF Core provider

Entity Framework Core has a Cosmos DB provider, and the first draft of this
design used it, because EF is the mapper on the relational side and one mapper
for three stores reads well. It was set aside for three reasons, each of which
is about this lane's subject.

**The provider hides the numbers this lane exists to show.** Every response from
the Cosmos DB SDK carries the request charge, the diagnostics, the number of
physical partitions a query touched and the query metrics. The provider surfaces
the charge in a log event and the rest not at all, and the store log on the
Admin tab wants all of it beside every operation.

**The mapping on this side is the serializer.** ADR: Data first chose EF as a
mapper so the data structure outlives the framework. On a document store there is
nothing to map: the document is the row, and what the JSON looks like is decided
by the serializer's naming policy and nothing else. A mapper between the domain
record and a document class, in the shape of `VehicleRows`, is the whole of it.

**The provider's own bites are the SDK's bites.** Synchronous I/O throws in
both, there are no joins in either, and the indexing policy is a property of the
container whichever client writes to it. Using the SDK does not avoid a single
one of the four things the design has to answer; it only makes the answers
visible.

So `TheYard.Infrastructure.Cosmos` is built on `Microsoft.Azure.Cosmos` directly:
one `CosmosClient`, authenticated with the container's managed identity, a
document type per container, and every read a point read or a query whose
charge is written down.

## The ports learn to wait

Every port in `Ports.cs` is synchronous, and the EF Core Cosmos provider has no
synchronous I/O: `ToList` and `SaveChanges` throw. That is the first decision of
this exercise that reaches the Application layer, and it gets its own record
(ADR: The ports learn to wait). The short version: the ports become
`Task`-returning, `BidService` trades its `lock` for a `SemaphoreSlim`, and the
host warms the inventory and the bids explicitly at startup. The alternative,
blocking on the task inside the adapter, is safe from deadlock in ASP.NET Core
and is still a performance defect on this container: it has one vCPU, the thread
pool starts with one thread, and a bid that blocks that thread while the store
answers is a bid that stalls every other request behind it. On a lane whose
subject is performance, that is not a shortcut worth taking.

## Source control means something different on this side

There is no schema and there are no migrations. What a container has instead is
a definition: its partition key path, its indexing policy, its unique keys and
its time to live, and that is what goes in the repository, as JSON under
`infra/cosmos/`, one file per container. A conformance test reads those files and
the code's own catalog of containers and fails the build when the names or
partition keys the adapters use disagree with the definitions, which is the same chain of authority ADR: Data
first built for SQL Server, with the SQL project replaced by the container files.

The application cannot create a container even if it wanted to. The Data
Contributor role it holds is a data-plane role, and creating a database or a
container is a control-plane operation that a data-plane token is refused for.
So the same rule holds on both sides for the same reason: the running application
maps to a store a person published, and refuses the store, falling back to files,
when what it maps to is not there.

## Where the tests run

CI has no Azure credential and is not getting one, so the suite's gate stays
SQLite. Three kinds of test cover the new store:

- **Definition tests**, which open no connection: the conformance test above,
  the port surface test, the document shape tests.
- **Store tests against the real account**, which run on the runner as Steve's
  signed-in principal against the `tests-` containers. They carry the trait
  `Store=cosmos` and CI filters them out by it, because a skipped test is a
  broken window in this repository and CI has no endpoint to run them against;
  on the runner they fail loudly if the endpoint is missing. The same shape as
  the SQL Server tests that assert the schema without a server, one step
  further out.
- **The browser suite, twice.** The 55 specs run against a local API started with
  the Cosmos configuration as well as without it. Same tests, both stacks, which
  is the sentence the goal asks for.

CI's coverage floor measures what CI runs, so the one assembly those filtered
tests cover, `TheYard.Infrastructure.Cosmos`, is left out of the number CI holds
to its floor, with the reason written beside the flag in `ci.yml`. The first CI
run of this work went red at 83.2 per cent of lines for code that had been
tested against the real account an hour earlier; naming the assembly hides only
the gap this runner cannot close, where lowering the floor would have hidden
every other one too.

## What does not carry over, so far

Found by reading the provider's documentation and this application's code
against each other before writing any of it. Each is a design decision, and each
lands in the record for the step it belongs to:

- `rowversion` has no counterpart; the concurrency token is the document's
  `_etag`, which the store maintains and the adapter sends back as `If-Match`
  on every replace. The retry loop in the bid store stays as it is.
- `ExecuteDelete` is not supported by the provider. A reset reads the caller's
  partition and deletes each document, which is one query pinned to a partition
  plus one point delete per bid.
- There is no unique index across partitions. Uniqueness of an email address is
  the claim document described above.
- `sys.tables` has no counterpart; the store is refused when a container is
  missing, read through the container's own metadata.
- `ORDER BY` needs an index on the sorted path, so the cold-start read loads the
  200 documents and orders them by `seq` in memory rather than paying for an
  index on every write.
- Synchronous I/O throws, which is the ports record.

## The fork, for Steve

Two shapes were possible and the numbers above price both.

**Load everything, as today.** Real parity, by construction, on a cold start of
250 documents. A visitor cannot tell the tabs apart because the request path is
the same code on both. It leaves the partition key question with no live example.

**Push the query down.** A filtered, sorted page served from the `catalogue`
container instead of the in-memory index. It puts the store on the hot path, it
makes the partition key and the indexing policy matter, and it costs a ten to
seventeen minute seed and a second implementation of search. On SQL Server the
same experiment would need the 100,000 rows persisted too, which is a schema
change to the SQL project and a publish, and that is a further piece of scope
priced separately if he wants both stores in the hot path.

**Recommendation, and what this lane does unless he says otherwise:** the first
shape for the site, to prove parity, and the second as a measured side experiment
on the catalogue query alone, behind an Admin card rather than in front of a
visitor, so both interview questions have a number behind them.

## Consequences

- One more Azure resource, $0.00 per month, with no key in existence. The
  standing rule against new resources was set aside for it by Steve on
  2026-09-08 ("I grant"), recorded in the lane's notes outside the repository.
- A second container group, about $34 a month at list price while it runs,
  drawn from the trial credit today, stoppable between comparisons.
- The ports change shape, and every adapter, the two services and the tests
  follow them.
- A third store in `YardConnection`, chosen by the presence of a Cosmos endpoint,
  which is a URL and not a credential.
- The records that describe a two-provider world, ADR: The relational store, ADR:
  Entity Framework explained, ADR: The SQL Server backend, ADR: Data first and
  ADR: Two providers explained, each get an addendum narrowing them to the
  relational side and pointing here, rather than being left to disagree quietly.

## Addendum, 2026-09-08: the estimates against the measurements

The table in "The arithmetic, on the parity path" was written before the
account existed. The measured numbers, from ADR: Measuring both stores and the
container's own store log:

| operation | estimated | measured |
| --- | --- | --- |
| seed, 200 vehicles + 50 photos, once | 1,500 to 2,500 RU, two to three seconds | 1,380 RU in 2,154 ms |
| cold start: read 250 documents | 400 to 700 RU, under one second | 1,190 ms for the catalogue load (charge in the store log) |
| one accepted bid | 6 to 8 RU | 6.52 RU (a miss and a create); 11.29 RU to raise it |
| one sign-in | 2 to 8 RU | 2.00 RU, every time |
| one registration | 10 to 12 RU | 13.04 RU |

The document weighs what the record said it would (5.52 RU to write a vehicle,
which is the "about five" the rule of thumb gives for a kilobyte with a minimal
index), and the estimates were within a request unit or two everywhere except
the seed, which came in under. The 100,000-document experiment came in over:
8.84 RU a document against the estimated 6, and 12.8 minutes against 10, with
the reasons in ADR: The partition key.

The fork was taken as recommended: the parity shape for the site, the
experiment behind an Admin card. The running monthly cost, from the measured
charges rather than the estimated ones, is $0.00 on the free tier, and the
whole measurement session, twenty rounds of everything a visitor does, cost
1,096 request units, which would be a twenty-seventh of a cent on serverless.

## Addendum, 2026-09-08: the version bump that took the live site to files

1.0.0.90 rolled both containers. The Cosmos DB one came up healthy after a
container check of 48,793 ms, which was forty-seven seconds longer than it
should have been. The Azure SQL one came up on files, twice, once on the roll
and once on a restart against a database that was demonstrably online, with
the same exception each time:

```
SqlException: A task was canceled.
 ---> TaskCanceledException at Azure.Core.Pipeline.RetryPolicy.WaitAsync
      at Azure.Core.Pipeline.HttpPipeline.SendRequestAsync
```

That is SqlClient acquiring its managed identity token, through Azure.Identity,
and being cancelled while the library was still retrying its way to the
identity endpoint. The new Cosmos project referenced Azure.Identity 1.21.0,
which pulled the whole application up from the 1.17.1 it had run on for a
week; the newer version spends longer finding the identity endpoint on
Container Instances than SqlClient's thirty seconds, so the connection was
cancelled mid-wait. The Cosmos DB SDK has no such ceiling, waited the whole
forty-nine seconds, and got its token, which is why one container survived and
the other did not.

Two things changed. The reference is pinned to 1.17.1, with the reason beside
it in the project file. And the deploy no longer writes `Connect Timeout=30`
into the connection string: `YardConnection` widens a timeout nobody gave to
sixty seconds, which is the budget the SQL Server record's arithmetic is built
on, and a value given by the deploy had been winning over it for a week
without anybody noticing. The live site ran on 1.0.0.88 for the forty minutes
between the finding and the fix, rolled back by hand with the same template
the deploy uses.

Two lessons, both already in this repository's records and both relearned:
a transitive version bump is a change to every project in the graph, not to
the one that asked for it; and a number a record reasons from has to be the
number the deploy actually sets.

A lesson relearned is a check that was missing, so the pin is now held by a
test as well as by a comment. It reads the version of the one Azure.Identity
assembly every project resolves, whichever of them asked for it, and fails in
the gate the day anything in the graph moves it, which is where 1.21.0 should
have been caught.

```live path=api/TheYard.Tests/PackagePinTests.cs region=pin
```

## Addendum, 2026-09-08: the store that remembers

The browser suite's run on Cosmos DB went red once late in the day, on the
test that watches the room answer a bid, after passing on every earlier ship.
The room was silent for forty-five seconds. The reason was not the store's
speed and not the room: it was that the store remembers.

On SQLite every run starts from an empty file, so the vehicle with the most
bids has had a handful of them, all placed that run. The `tests-` containers
keep every run's bids for a day, and every run's bidding tests open the same
most-bid vehicle and raise it a few increments. By the evening the 2021 Ram
1500 at the top of that sort stood at $78,000 against an opening ask of
$36,500, and the room stops at twice the opening ask (ADR: Competing bidders).
A human could still bid on it, and did; the room, correctly, would not answer.
Read off the test store with the API booted against it, not guessed.

The fix is in the helper every bidding spec goes through: it reads the
listing through the API and opens the first live vehicle the room can still
answer on, with two increments of room under the ceiling and under buy-now and
five minutes on the clock, instead of the first card regardless. On a fresh
store that is the same card it always opened. The test that holds it is the
market spec itself, run on the store whose top vehicle stands at its ceiling.

```live path=tests/e2e/bidding.ts region=room-to-answer
```

What this says about the gate is worth keeping: a suite that runs against a
store with a memory is a different suite from one that runs against a fresh
file, and any test that assumes a quiet field will find that out on the day
the field fills up. The one-day time to live keeps the containers from growing
without bound; it does not make a run start clean, and nothing here pretends
it does.

## Addendum, 2026-09-08: the second store stopped being instead of the first

Later the same day the two stores moved into one process (ADR: One container,
both stores). For this record that changes one sentence and one gate. The
sentence: `YardConnection.Choose` no longer picks Cosmos DB instead of the
relational store; a container runs every store it is configured for, and the
endpoint setting adds a backend rather than replacing one. The gate: "the
whole suite booted on Cosmos DB" now boots every application on both stores
with Cosmos DB as the default, which exercises the per-request selection on
every test as well as the store. The cost arithmetic above is unchanged, and
so is the free tier; the second container now also opens the SQL Server
connection the first one opens, with the same identity.

## Addendum, 2026-09-09: what the gate runs on the document store now

"The browser suite, twice" above described the gate at 1.0.0.89. Since
1.0.0.97 the gate runs every suite once per store and its two sides at the
same time (ADR: The five-minute gate): the whole xUnit suite booted on the
document store, the six store tests against the real account in their own
step, and on the document store the three browser spec files whose
behaviour depends on the store, the toggle, the accounts and the Admin tab's
store log; the other ten run on SQLite in the same gate, because nothing in
them reads a store. Same sentence, five minutes instead of twenty.

## Files

- The written pre-approval is in the lane's notes outside the repository, because it names principals.
- [`api/TheYard.Application/Ports.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Ports.cs): the three seams the second store implements.
- [`api/TheYard.Application/InventoryService.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/InventoryService.cs): where the store stops being on the request path.
- [`api/TheYard.Infrastructure/SyntheticVehicleSource.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/SyntheticVehicleSource.cs): 200 documents becoming 100,000 in memory.
- [`docs/ADR-058-the-partition-key.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-058-the-partition-key.md): the one decision that cannot be changed later.
- [`docs/ADR-039-sql-server-backend.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-039-sql-server-backend.md): the store this one sits beside, and the pricing method this record copies.
