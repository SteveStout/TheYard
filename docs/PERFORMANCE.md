# Performance on the smallest machine that will hold it

Every number on this page was measured by the running application against itself, on the container that
serves this site, and every one of them links to the record that holds the method. Nothing here is a
benchmark run on a laptop and quoted afterwards.

The claim this page makes is narrow and checkable: **a hundred thousand vehicles, two different database
engines, and page work measured in single-digit to low-double-digit milliseconds, on free-tier data stores
and one small container.**

## What it runs on

| Resource | What it is | What it costs |
| --- | --- | --- |
| Azure Cosmos DB | Free tier, 1000 RU/s shared, local auth disabled so no key exists | **$0.00 a month** |
| Azure SQL Database | Serverless, auto-pause after an idle hour, Entra-only | free-trial credit |
| Container | One Azure Container Instance, 1 vCPU and 1.5 GB | about $34 a month at list price while it runs |
| Edge and TLS | Netlify free plan, 300 build credits a month | **$0.00 a month** |
| Storage, 100,000 vehicles | 82 MB against a 25 GB allowance | **$0.00** |

The second container, the one serving the document store on its own address, is started for a comparison
and stopped afterwards, which is the only reason the container line is not also zero.

Sources: [A second store, priced](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md),
[Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-007-edge-economics.md), [The partition key](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-058-the-partition-key.md).

## What that buys, measured

The application times itself. `POST /api/admin/proof` runs eight paired rounds of everything a visitor
does, alternating which store goes first so the ordering cannot flatter either one, and the Admin tab
shows the result. These are the medians from the live site's own container at 1.0.0.94:

| What a visitor does | Azure SQL Database | Azure Cosmos DB |
| --- | --- | --- |
| Open a vehicle | **1 ms** | **1 ms** |
| Load the filter values | **12 ms** | 29 ms |
| The listing page, 100 of 100,000 | **51 ms** | 56 ms |
| Place a bid | 84 ms, 2 statements | **10 ms**, 2 operations, 6.52 RU |
| Raise a bid | 85 ms, 2 statements | **9 ms**, 2 operations, 11.29 RU |
| Sign in | 122 ms, 1 statement | **82 ms**, 2 operations, 2.00 RU |
| Register | 233 ms, 3 statements | **133 ms**, 4 operations, 13.04 RU |

The pages that never touch a store are the same on both, to the millisecond.

Source: [The same performance, proven](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-067-same-performance-proven.md),
[Measuring both stores](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-064-measuring-both-stores.md).

## The honest part, and it is the interesting part

**The two engines are not different. The distance to them is.**

On 3 of the 8 paths the two stores answer in the same time. On the other 5 the whole difference is the
round trip to the store: **39 ms to Azure SQL Database and 2 ms to Azure Cosmos DB.** Take one round trip
per statement or per operation off each side and what is left is within a few milliseconds either way.

The relational server sits one region away from the container and every statement crosses that gap. The
document account is in the container's own region. A bid is two statements or two operations, so the
relational side pays the gap twice and the card shows exactly that.

**That is a hosting fact, not a database fact**, and it is worth saying plainly rather than quoting the
faster column and letting a reader assume the engine won. The same run was repeated on the second
container, whose default store is the document one, and it produced the same shape on every row.

Read again on 13 September, off the second container's own card from a run on 1.0.0.112: 41 ms to Azure
SQL Database and 2 ms to Azure Cosmos DB, 3 of 8 paths the same, 4 more differing by exactly the round
trip, and one row, Register, differing by more on a single sample, 950 ms against 153. One sample at that
size on a serverless database that pauses when idle says nothing about a statement's cost either way, and
the card says "1 sample" beside it. The table above keeps the 1.0.0.94 run because both stores were warm
and both containers were measured within a quarter of an hour; the later card is quoted so a reader can
see the shape hold, and the one row that did not.

## Where the milliseconds actually came from

Three decisions, each with its number.

**The index is paid for on every write and earns only on the queries that use it.** Leaving the document
store's indexing policy at its default charges **16.07 request units a document** on the bulk seed against
**8.84** with the policy trimmed: 21 minutes against 13, and 8,407 documents refused by throttling that the
tuned seed never saw. The same seven queries then cost within half a request unit of each other on both.
([Cosmos DB explained](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-065-cosmos-db-explained.md))

**A partition key is chosen by its worst case, not its average.** `/make` has fifteen values with the
largest at nine per cent, so the biggest logical partition at a hundred thousand documents is about
**7 MB against a 20 GB cap** and no partition is hot. `/body_style` would have put 53 per cent of every
read on one partition. ([The partition key](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-058-the-partition-key.md))

**Only the default store warms before serving.** Loading both catalogues at start-up doubled every test
application's memory and turned a two-minute suite into a thirty-minute crawl that looked like a hang.
([One container, both stores](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-066-one-container-both-stores.md))

## What it cost to keep it honest

Measuring is not free either, and the bill is small enough to print: the whole twenty-round measurement
session cost the document store **1,096 request units**, which is about a twenty-seventh of a cent on
serverless pricing.

On the edge, eleven production deploys had quietly eaten **165 of the 300 free credits in a month at 15
each**, while actually serving the site cost almost nothing. Application pushes no longer redeploy the
edge, so they cost **zero credits**. ([Edge economics](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-007-edge-economics.md))

## Check any of it yourself

- Open the **Admin** tab on either site: live health checks per store, the paired-round comparison card,
  the container's recent events, and every store operation the application has made with how long it took.
- `POST /api/admin/proof` starts a fresh run; `GET /api/admin/proof` reads the one in progress or the last
  one finished.
- Every figure above links to the record that holds the method, and the code samples inside those records
  are read from the running build rather than pasted, so a record cannot drift from the code it describes.

## Files

- [`api/TheYard.Api/Proof.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Proof.cs): the paired rounds, alternating which store goes first.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the comparison card and the proof card the numbers above are read from.
- [`infra/cosmos`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): the container definitions, indexing policy and partition key included.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the one container, 1 vCPU and 1.5 GB.
