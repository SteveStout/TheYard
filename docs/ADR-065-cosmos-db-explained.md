# ADR: Cosmos DB, explained for someone who knows SQL Server

Status: written 2026-09-08, for 1.0.0.90. The newcomer's record for the second
store, in the shape of ADR: Two providers and a SQL project, explained: written
for a reader who knows SQL Server cold and this not at all, and organised around
the one question the store makes you answer on every design decision, which is
what it costs. The decision and the arithmetic are in ADR: A second store on
Cosmos DB, and what it costs; the numbers below come from ADR: Measuring both
stores and ADR: The partition key, and say so where they do.

## What is actually running

Two containers, one image. The first, the site at `theyard.stevenstout.biz`,
keeps its catalogue, its bids and its accounts in Azure SQL Database. The
second, on its own Azure address, keeps the same three things in Azure Cosmos
DB, in a database called `theyard` with four containers in it. Both authenticate
as the same managed identity; neither has a password or a key anywhere.

The application above the store is the same code. The three ports in `Ports.cs`
are what each store implements, and nothing above them knows which one answered.
That is the whole reason the comparison is fair.

## The vocabulary, in the order you will meet it

**Account.** The thing you create in Azure, like a logical server. It has an
endpoint, a default consistency level, and one or more regions. Ours has one
region, West US 2, and no keys.

**Database.** A namespace with a throughput budget. Ours holds 1000 RU/s, shared
by every container in it, which is the whole of the free tier's allowance.

**Container.** The nearest thing to a table, and the comparison stops there. A
container has no columns and no schema. It has a partition key path, an
indexing policy, and optionally a time to live and unique keys, and that is
everything you can say about it. Every document in it can be a different shape.

**Document.** A row, if a row were a JSON object with no fixed columns. Ours are
in snake case, like the API, and you can read one beside the dataset it was
seeded from and see the same fields. Every document has an `id`, and the pair
of `id` and partition key value is its primary key. The system adds `_etag`,
`_ts` and a few others.

**Request unit (RU).** The unit everything is priced in, and the one idea in
this record worth taking to an interview. A request unit is a normalised
measure of the CPU, memory and I/O a request consumed. A point read of a 1 KB
document costs 1 RU. Everything else is measured against that: a write of the
same document about 5 RU with a minimal index and more with the default one, a
query the cost of its index lookup plus the documents it loaded, an aggregate
the cost of everything it touched. The service tells you the charge on every
response, and this application writes it down beside every operation on the
Admin tab (ADR: What the store is actually doing). Throughput is bought in RU
per second: 1000 RU/s means the database will do a thousand request units of
work a second and answer 429 to the thousand-and-first, and the SDK backs off
and retries.

**Partition key.** The path in every document whose value decides where the
document lives. All documents sharing a value are a logical partition; logical
partitions are packed onto physical partitions, each of which serves up to
10,000 RU/s and holds 50 GB. A read that names the key and the id is a point
read, the cheapest thing there is. A query that names the key runs inside one
partition. A query that does not fans out to every physical partition. The key
cannot be changed after the container exists, and choosing it is the design
(ADR: The partition key).

**Indexing policy.** By default the service indexes every path of every
document, which makes every property queryable and makes every write pay for
every index entry. The policy is where you say otherwise. Ours excludes
everything on the four parity containers, because nothing queries them by a
property, and includes exactly the filter and sort paths on the experiment
container. This is the single biggest cost lever on this workload.

**Consistency level.** Five of them, from strong to eventual, and the account
picks a default. Ours is session: a client reads its own writes, in order,
which is what a bidder needs, and reads cost 1 RU per KB. Strong and bounded
staleness read from a quorum of replicas and cost double. Consistent prefix and
eventual cost the same as session and promise less. The application asks for
session explicitly in code so the choice is not only in the portal.

**Etag.** The concurrency token, kept by the store, changed on every write. A
replace that sends the etag it read is refused with 412 if the document moved,
which is what `rowversion` does on SQL Server with a different spelling
(ADR: The SQL Server backend, addendum).

## Point read against query

The distinction the whole store is built around, and the one a SQL Server
reader has least reason to have met.

A point read is `ReadItem(id, partitionKey)`. The service goes straight to the
partition, straight to the document, and charges about 1 RU per kilobyte. A
query, even `SELECT * FROM c WHERE c.id = @id`, is a query: it is parsed,
planned, sent to each physical partition it could touch, answered by the index,
and charged for the index lookup and the documents loaded. On this application
the difference is a sign-in that costs about 2 RU as two point reads against
one that would cost about 3 as one query, and the design of the accounts
container exists to make every account operation a point read (ADR: Accounts
on a document store).

## What a hot partition looks like

A key whose most common value holds half the data. `body_style` here has five
values and SUV is 53 per cent of them, so a container partitioned on it would
put 53,000 of the 100,000 experiment documents in one logical partition. Every
write during the seed and every read that names SUV lands on the one physical
partition that holds it, while the rest sit idle, and no amount of provisioned
throughput helps because a logical partition can never use more than the
physical partition it sits on. `make` has fifteen values and the largest is nine
per cent, which is why it was chosen (ADR: The partition key).

## What it costs to leave the indexing policy at the default

The seed is where it shows. A document is written once and its index entries
are written with it, so the default policy, which indexes every path, charges
for thirty-odd index entries on every one of the 100,000 experiment documents
where the tuned policy charges for nine: 16.07 request units a document
against 8.84, 21 minutes against 13, and 8,407 documents refused by throttling
that the tuned seed never saw, measured by seeding the same 100,000 documents
into two containers that differ only in the policy (ADR: The partition key).
The same seven queries then cost within half a request unit of each other on
the two containers, which is the whole lesson in one line: an index is paid for
on every write and earns only on the queries that use it. On the four parity containers there is no index at
all, which is the same decision ADR: The relational store made about SQL
Server: they are read whole or by key, so an index would cost every write and
earn nothing.

## Where the document model won, and where it lost

Won: the bid write and the sign-in are point operations in the same region as
the container, two request units for a sign-in and six and a half for a bid,
and the numbers in ADR: Measuring both stores are the numbers.
The store's cost is visible on every response, which no relational engine
offers, and the Admin tab shows it. The account has no keys and needed none.
The whole month is $0.00 on the free tier, and would be a cent on serverless.

Lost: uniqueness is a document you write rather than a constraint you declare.
There is no cascade, so deleting an account would leave its bids. There is no
`ORDER BY` without an index on the sorted path, and no join at all, so the
document has to carry what it needs. A synchronous port cannot be implemented
against it, which reached the Application layer (ADR: The ports learn to wait).
And the default view of this site, sorted by ending soonest, cannot be pushed
down at all, because the schedule is derived from the clock and the id rather
than stored, which is a fact about this application's design that the store
made visible.

## Which one to choose for this workload

This application reads its catalogue once at startup and serves everything from
memory, writes one document per accepted bid, and signs people in. Measured
from Missouri, a bid takes 135 ms on the document store and 219 on the
relational one, a sign-in 221 against 249, and the pages a visitor spends most
of their time on are equal to within a few milliseconds because neither store
is on their path (ADR: Measuring both stores). The document store's lead is
geography: its account is in the container's own region and the SQL server is
one region away, because that region refused to create one. Both cost $0.00.
On this workload the honest answer is that the store is not the performance
question; the in-memory index is, and it was answered in ADR: The search index. Where the two differ is what you are buying. Azure SQL Database
buys constraints, joins, a schema in source control as DDL and a query planner
that will sort by anything. Cosmos DB buys a cost model you can see, point reads
at single-digit milliseconds anywhere in the world if you add regions, and the
freedom to change a document's shape without a migration. For a used-vehicle
auction whose catalogue fits in memory and whose writes are bids, the relational
store is the one whose guarantees this application actually uses, and the
document store is the one whose performance it would need at a scale it does not
have. That is the sentence for the interview, and the numbers behind it are
measured.

## Addendum, 2026-09-09: the proof took the networks out

With both stores in one container (ADR: One container, both stores), the
container measured itself (ADR: Same performance, proven): the same request,
the same process, the same request ring, only the store different, and the
round trip to each store measured on its own. On the pages that never touch a
store the two are the same. On every path that does, the whole difference is
the round trip, 39 ms per statement to the relational server one region away
and 2 ms per operation to the document account in the container's own region;
take one round trip per operation off each side and the two stores are the
same on every row. So the sentence above stands and gets sharper: on this
workload the performance question is where the store is. A relational server
in the container's region would close the gap the document store currently
enjoys, and nothing in the rows says the document store would keep it.

## Files

- [`docs/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md): the decision and the arithmetic.
- [`docs/ADR-058-the-partition-key.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-058-the-partition-key.md): the key, the alternatives, and the measured queries.
- [`docs/ADR-064-measuring-both-stores.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-064-measuring-both-stores.md): both containers in one session, with the method.
- [`api/TheYard.Infrastructure.Cosmos/CosmosStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosStore.cs): the one connection, and the wrapper that writes every charge down.
- [`infra/cosmos/`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): what a container is, in six JSON files.

## Addendum, 2026-09-09: the comparison, laid out beside this

This record teaches the vocabulary and gives the choice; the evidence for the
choice is now laid out in one place, row by row, with Azure SQL Database on
the left and Azure Cosmos DB on the right: the same bid at rest in both, the
same write in both adapters as live code, the same guarantee given by one
engine and built on the other, the measured numbers and the cost, each row
naming the record that decided it, and the reading behind each row (ADR: SQL
Server and Cosmos DB, side by side). Steve asked for it in as many words, "a
diagram/analysis of SQL Server vs Cosmos DB where the left side is SQL Server
and the right side is cosmos DB". Read this record first and that one second;
the drawing on that record's own page is the one to keep open in an interview.
