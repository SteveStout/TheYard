# SQL Server and Cosmos DB, side by side

One page puts the two stores this application runs on next to each other,
Azure SQL Database on the left and Azure Cosmos DB on the right, row by row:
the same bid at rest in both, the same write in both adapters as live code,
the numbers the records measured, the record that decided each row, and the
reading behind it. Read ADR: Cosmos DB, explained for someone who knows SQL
Server first; it teaches the vocabulary, and this page is the comparison a
reader makes after learning it.

## Why this page exists

TheYard runs on two stores at once, Azure SQL Database and Azure Cosmos DB,
each serving one of its two sites, and the question a reader of the records
asks first is how the two compare on the same work. That comparison deserves
a page of its own, at the top of the sidebar beside Hosting, rather than a
place inside the decision records: nothing here was decided, it lays out
decisions already made, side by side, with the code and the numbers behind
them.

The material existed and was spread across eleven records. The SQL Server side
was decided over four days (ADR: The SQL Server backend; ADR: Data first, and
the database in source control; ADR: Two providers and a SQL project,
explained; ADR: What the database is actually doing), the Cosmos DB side over
two (ADR: The partition key; ADR: A second store on Cosmos DB, and what it
costs; ADR: The ports learn to wait; ADR: Accounts on a document store; ADR:
What the store is actually doing), and the comparison itself over the night
between (ADR: Backends, side by side; ADR: Measuring both stores; ADR: Same
performance, proven). A reader who wants the two stores next to each other
had to hold all of them open. This page is that reading, done once, with
the code pulled in live so it cannot drift from what runs, and the diagram
on its own page for a screen or an interview.

Who it is for: a developer who knows one of these stores well and is meeting
the other, which describes most of the people who will read it. Every row is
a question that developer asks first.

## The picture

[![SQL Server and Cosmos DB, side by side: ten rows, Azure SQL Database on the left and Azure Cosmos DB on the right, each box naming the file in this repository that answers it and each number one a record measured](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sql-vs-cosmos.png)](https://theyard.stevenstout.biz/api/docs/diagrams/sql-vs-cosmos)

*A preview. [Open the side-by-side diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/sql-vs-cosmos)
to zoom in and read across. It is drawn by `docs/images/sql-vs-cosmos.mjs`
from the same facts this page cites, and redrawn when a store, a file or a
number changes; a picture that disagrees with the records is a bug in the
picture (ADR: Docs and testing).*

## The same application, both stores

Nothing above the adapters knows which store it is on. The Application layer
speaks to three ports, a vehicle source, a photo manifest source and a bid
store, and Identity speaks to a user store; each side implements all four
(ADR: The ports learn to wait). One container
image runs both adapters in one process, brought up side by side, and each
container group serves one of them by default: the live site,
https://theyard.stevenstout.biz, serves Azure SQL Database, and the second
site, https://theyard-cosmos.stevenstout.biz, serves Azure Cosmos DB, with the
Store bar linking each to the other at the same page (ADR: One container, both
stores; ADR: A permanent address for the second site). So every comparison
below is the same code path, the same requests and the same measuring
instrument, with only the store changed. That is what makes the numbers
comparable, and it is the reason the second store was built as a complete
backend rather than a demo (ADR: A second store on Cosmos DB, and what it
costs).

## Row by row

Each row names the records that decided it. Open one from the sidebar, or
follow its address.

| The question | Azure SQL Database | Azure Cosmos DB for NoSQL | Decided in |
| --- | --- | --- | --- |
| What it is | A relational database: tables, rows, foreign keys, T-SQL. The schema is the contract and the engine enforces it. General Purpose serverless on the free limit, one region away from the container. | A document database: JSON documents in containers, each container split by a partition key. The document owns its shape; the engine enforces the key, the id and the etag. Free tier, in the container's own region. | [ADR-039](https://theyard.stevenstout.biz/?doc=adr-sql-server), [ADR-059](https://theyard.stevenstout.biz/?doc=adr-second-store), [ADR-065](https://theyard.stevenstout.biz/?doc=adr-cosmos-explained) |
| How the application reaches it | Entity Framework Core over SqlClient: a `DbContext` per request, LINQ turned into statements, a managed identity token instead of a password. An interceptor records every statement. | The Cosmos DB SDK directly: one `CosmosClient` for the process, point reads and queries written by hand so the request charge stays visible. Managed identity with a data-plane role; local authentication disabled, so no key exists. | [ADR-039](https://theyard.stevenstout.biz/?doc=adr-sql-server), [ADR-043](https://theyard.stevenstout.biz/?doc=adr-sql-visible), [ADR-062](https://theyard.stevenstout.biz/?doc=adr-store-visible) |
| A bid, at rest | One row in `dbo.Bids`: `PRIMARY KEY (UserId, VehicleId)`, a foreign key to `AspNetUsers` with `ON DELETE CASCADE`, a `rowversion` the engine maintains. | One document in `bids`: `id` is the vehicle, the partition key `/user_id` is the buyer, the etag is the concurrency token. No foreign key exists; the indexing policy excludes every path. | [ADR-040](https://theyard.stevenstout.biz/?doc=adr-data-first), [ADR-058](https://theyard.stevenstout.biz/?doc=adr-partition-key) |
| Writing a bid | Find, then add or update, then `SaveChanges`: two statements. A `DbUpdateConcurrencyException` means another writer moved the row; start again from what is there, three tries. | Point read, then `CreateItem` or `ReplaceItem` with `If-Match`: two operations, 1 RU plus 5.52 or 10.29. A 412 or a 409 is the same race; the same three tries. | [ADR-039](https://theyard.stevenstout.biz/?doc=adr-sql-server), [ADR-059](https://theyard.stevenstout.biz/?doc=adr-second-store) |
| Keeping an address unique | A unique index on `NormalizedUserName` refuses the second account. Nothing in the application has to remember to check. | No unique index spans partitions. A claim document whose id is the address is created first; a 409 on that create is the refusal, and a failed account write removes the claim again. | [ADR-061](https://theyard.stevenstout.biz/?doc=adr-accounts-documents) |
| Indexes, and what a query costs | Indexes chosen per query in the DACPAC, a plan the engine picks, and a cost that shows up as compute time against the free limit. | An indexing policy per container as JSON, every path excluded here because the site reads by id, and a cost in request units per call. The experiment: 8.84 RU per seeded document under the tuned policy, 16.07 under the default, and nothing bought on any of seven queries. | [ADR-040](https://theyard.stevenstout.biz/?doc=adr-data-first), [ADR-058](https://theyard.stevenstout.biz/?doc=adr-partition-key) |
| Consistency and transactions | ACID across any number of tables in one transaction; row versioning keeps readers off writers; the database is the arbiter. | Session consistency; a transactional batch is atomic inside one partition, which is how a reset deletes one buyer's bids as one call. Nothing spans partitions atomically, and the application is arranged so nothing needs to. | [ADR-060](https://theyard.stevenstout.biz/?doc=adr-ports-wait), [ADR-065](https://theyard.stevenstout.biz/?doc=adr-cosmos-explained) |
| What the Admin tab shows | Every statement with its milliseconds and its parameters by name, on the SQL card and in the console log under `Microsoft.EntityFrameworkCore.Database.Command`. | Every operation with its request units, its milliseconds and whether it named a partition, on the operations card and, since 1.0.0.103, as one console line each under `TheYard.Infrastructure.Cosmos.CosmosStore`. | [ADR-043](https://theyard.stevenstout.biz/?doc=adr-sql-visible), [ADR-062](https://theyard.stevenstout.biz/?doc=adr-store-visible), [ADR-063](https://theyard.stevenstout.biz/?doc=adr-backends) |
| Measured on this application | From Missouri, p50: bid write 219 ms, sign in 249 ms, register 319 ms. From the container: bid write 84 ms, and 39 ms of every operation is the round trip to a server one region away. | From Missouri, p50: bid write 135 ms, sign in 221 ms, register 216 ms. From the container: bid write 10 ms, 6.52 RU, a 2 ms round trip, and every sign-in exactly 2 RU. | [ADR-064](https://theyard.stevenstout.biz/?doc=adr-measuring-stores), [ADR-067](https://theyard.stevenstout.biz/?doc=adr-proof) |
| What it costs this month | $0.00 on the free limit: 100,000 vCore-seconds and 32 GB a month. Serverless compute pauses when nobody visits, and the first request after a quiet stretch waits for it. | $0.00 on the free tier: 1000 RU/s and 25 GB for the life of the account, never paused. The second container group that serves it is about $34 a month at list. | [ADR-039](https://theyard.stevenstout.biz/?doc=adr-sql-server), [ADR-059](https://theyard.stevenstout.biz/?doc=adr-second-store) |

The proof's verdict, from the container itself rather than from a client
(ADR: Same performance, proven): on three of eight paths the two stores
answer in the same time, and on the other five the difference is the round
trip to the store, 39 ms to Azure SQL Database and 2 ms to Azure Cosmos DB;
taking one round trip per operation off each side leaves them the same.

## The same bid, as it is stored

The row and the document, read from this build. The left declares what the
engine will refuse; the right declares which path the engine will index,
which here is none, and says why in the file itself.

```live path=api/TheYard.Database/Tables/Bids.sql region=*
```

```live path=infra/cosmos/bids.json region=*
```

## The same write, in both adapters

The relational bid store, then the document bid store. Read the two
`SaveAsync` methods against each other: the loop is the same, the exception it
catches is the store's way of saying the same thing, and the document side's
comment names the relational rule it follows.

```live path=api/TheYard.Infrastructure/EfSources.cs region=bid-store
```

```live path=api/TheYard.Infrastructure.Cosmos/CosmosSources.cs region=cosmos-bid-store
```

## The same guarantee, given and built

Uniqueness of an address. On the relational side it is one line of DDL and the
engine's job; on the document side it is a method, because no index spans
partitions (ADR: Accounts on a document store).

```live path=api/TheYard.Database/Tables/Identity/AspNetUsers.sql region=*
```

```live path=api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs region=create
```

## Where the two are the same

The application does not care. The three ports are the same interfaces on
both sides; `BidService` serializes simultaneous bids the same way whichever
store is underneath; a sign-in is Identity's own code on both, with a
different `IUserStore` behind it. Both are reached with a managed identity
and no secret. Both are logged, statement for operation, into the same ring
the Admin tab reads. Both cost nothing this month. And on the pages a visitor
spends most of their time on, the listing, the vehicle and the filter values,
neither store is on the path at all, because the catalogue is served from
memory, so those pages measure the same to within a few milliseconds on both
sites (ADR: Measuring both stores).

## Where they differ, and what that buys

The relational side buys guarantees the application uses: a foreign key that
takes an account's bids with it, a unique index that is nobody's job to
remember, a transaction that can touch every table, a schema in source control
as DDL, and a planner that will sort by anything. The document side buys a
cost model you can see on every response, point reads at single-digit
milliseconds in the container's own region, a shape you can change without a
migration, and the freedom to add regions if the site ever needed them. What
the document side charges for those is visible on this page: the claim
document that stands in for a unique index, the cascade that does not exist,
the sort that needs an index on the sorted path, and the synchronous port that
had to learn to wait (ADR: The ports learn to wait).

The measured difference between them on this workload is geography, not
engine: the SQL server is one region away because that region refused to
create one, and the Cosmos DB account is next door. With the round trips taken
out the two are the same, which is the sentence the proof record was written
to be able to say (ADR: Same performance, proven). The honest choice for a
used-vehicle auction whose catalogue fits in memory and whose writes are bids
is the one whose guarantees it uses, and that reasoning is ADR: Cosmos DB,
explained for someone who knows SQL Server; this page is the evidence laid
out beside it.

## Reading

Microsoft's own documentation for each row, thirty-six pages, every one read
on 2026-09-09 before it was linked here (three more that answered 404 that
morning were left out). The relational side first, then the document side, then the
guides that compare them.

Azure SQL Database and Entity Framework Core:

- [What is Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/sql-database-paas-overview)
- [The free offer for Azure SQL Database](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer): the 100,000 vCore-seconds and 32 GB this site runs on, and what happens when they run out.
- [Serverless compute tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-overview): auto-pause and the resume the application budgets for.
- [Microsoft Entra authentication for Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-overview): how a managed identity signs in without a password.
- [Primary and foreign key constraints](https://learn.microsoft.com/en-us/sql/relational-databases/tables/primary-and-foreign-key-constraints) and [clustered and nonclustered indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/clustered-and-nonclustered-indexes-described).
- [rowversion](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql): the column behind the bid row's optimistic concurrency, and [the transaction locking and row versioning guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide).
- [Execution plans](https://learn.microsoft.com/en-us/sql/relational-databases/performance/execution-plans) and [monitoring with dynamic management views](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs): where a relational query's cost is read.
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/), [handling concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and [interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors): the exception the bid store retries on, and the hook the SQL card is built on.

Azure Cosmos DB for NoSQL:

- [What is Azure Cosmos DB](https://learn.microsoft.com/en-us/azure/cosmos-db/introduction)
- [Request units](https://learn.microsoft.com/en-us/azure/cosmos-db/request-units): the unit every number on the right column is priced in, and [optimizing the cost of reads and writes](https://learn.microsoft.com/en-us/azure/cosmos-db/optimize-cost-reads-writes).
- [Partitioning](https://learn.microsoft.com/en-us/azure/cosmos-db/partitioning-overview) and [a worked modeling and partitioning example](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/how-to-model-partition-example): why the bid's partition is the buyer.
- [Indexing policies](https://learn.microsoft.com/en-us/azure/cosmos-db/index-policy): what "every path excluded" means and costs.
- [Consistency levels](https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels), [managing consistency](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/how-to-manage-consistency) and [transactions and optimistic concurrency](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/database-transactions-optimistic-concurrency): the etag and the 412.
- [Unique keys](https://learn.microsoft.com/en-us/azure/cosmos-db/unique-keys): scoped to a partition, which is why the address needed a claim document.
- [Free tier](https://learn.microsoft.com/en-us/azure/cosmos-db/free-tier) and [serverless](https://learn.microsoft.com/en-us/azure/cosmos-db/serverless): the two ways this store could have cost nothing.
- [Role-based access to the data plane](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/security/how-to-grant-data-plane-role-based-access): the role the managed identity holds instead of a key.
- [Reading an item](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/how-to-dotnet-read-item), [querying items](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/how-to-dotnet-query-items), [query metrics](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query-metrics) and [the .NET SDK best practices](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/best-practice-dotnet): the calls the adapter makes and the singleton client rule it follows.
- [Bulk import in .NET](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/tutorial-dotnet-bulk-import): how the partition key experiment seeded 100,000 documents twice.
- [Joins](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query/join) and [subqueries](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/query/subquery) in the query language: a join here is inside one document, which is the thing a relational developer meets first.

Choosing between them:

- [Understanding the differences between NoSQL and relational databases](https://learn.microsoft.com/en-us/azure/cosmos-db/relational-nosql), Microsoft's own framing of the question this page answers for one application.
- [Choose a data store](https://learn.microsoft.com/en-us/azure/architecture/guide/technology-choices/data-store-overview) and [criteria for choosing a data store](https://learn.microsoft.com/en-us/azure/architecture/data-guide/technology-choices/data-storage), from the Azure Architecture Center.

## Keeping it true

- The comparison has one address on each site, and a picture that is a claim
  about the repository rather than an illustration: the diagram names files,
  and the page shows those files live.
- A change to either adapter's bid store changes this page on the next
  request, because the code is read from the build; a change to a number in
  ADR-064 or ADR-067 has to be carried here by hand, and the drawing redrawn.
- This is the page to send a reader who asks "why not just one database":
  the answer is in the last two sections, with the measurements above them.

## Files

- [`docs/images/sql-vs-cosmos.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/sql-vs-cosmos.mjs): draws
  [`docs/images/sql-vs-cosmos.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/sql-vs-cosmos.svg), served on its own page at
  `/api/docs/diagrams/sql-vs-cosmos`, and the PNG preview beside it.
- [`api/TheYard.Database/Tables/Bids.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Bids.sql) and
  [`infra/cosmos/bids.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/bids.json): the same bid at rest.
- [`api/TheYard.Infrastructure/EfSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfSources.cs) and
  [`api/TheYard.Infrastructure.Cosmos/CosmosSources.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosSources.cs): the same write.
- [`api/TheYard.Database/Tables/Identity/AspNetUsers.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/Identity/AspNetUsers.sql) and
  [`api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosUserStore.cs): the same guarantee.
- [`api/TheYard.Infrastructure/SqlLogInterceptor.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/SqlLogInterceptor.cs) and
  [`api/TheYard.Infrastructure.Cosmos/CosmosStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosStore.cs): what each side records for the Admin tab.
- [`api/TheYard.Tests/SideBySidePageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SideBySidePageTests.cs): holds this
  page to its promise, a live sample from each side of every pair and a link
  to every record it compares.
