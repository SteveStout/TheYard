# ADR: The partition key

Status: proposed, 2026-09-08, written before the containers existed, because
this is the one decision on a Cosmos DB container that cannot be changed
afterwards. The numbers at the bottom are filled in by measurement (ADR:
Measuring both stores) and the record says which are estimates until then.
Parent: ADR: A second store on Cosmos DB, and what it costs.

## What the key is, for a reader who knows SQL Server

A SQL Server table has a clustered index that decides its physical order, and
this project chose `Seq` for the catalogue because the only query it serves reads
the table in that order (ADR: The SQL Server backend). A Cosmos DB container has
no order and no clustered index. What it has instead is a partition key: one
path in every document whose value decides which logical partition the document
lives in. Every document with the same value is one logical partition, capped at
20 GB. Logical partitions are packed onto physical partitions, each of which
serves up to 10,000 RU/s and holds up to 50 GB, and the service splits physical
partitions as data or throughput grows.

Two things follow, and they are the whole subject:

- A read that names the key value and the id is a point read, the cheapest
  operation the service has, about 1 RU for a 1 KB document. A query that names
  the key value runs inside one logical partition. A query that does not name it
  fans out to every physical partition, is answered by each and merged, and pays
  for the index lookup on each.
- The key is permanent. Changing it means creating a new container and copying
  every document across. So the choice is made by writing down the queries the
  application actually runs, not the ones it might.

## The queries this application actually runs

Read out of the code, per container, on 2026-09-08.

| container | reads | writes | key | why |
| --- | --- | --- | --- | --- |
| `vehicles` | all 200 documents, once, at cold start | seed, once | `/make` | read whole, so any key serves; `/make` keeps it the same shape as `catalogue`, which is where the choice is tested |
| `photos` | all 50 documents, once, at cold start | seed, once | `/style` | the loader groups them by style |
| `bids` | all documents once at cold start; one document by (user, vehicle) before each write | one point write per accepted bid; one delete per bid on reset | `/userId` | every write is pinned; a reset is one query inside one partition plus point deletes; the cold-start read is the only cross-partition operation and it runs once |
| `users` | one document by id on "who am I"; one claim document by address, then one user document, on sign-in | two creates on register; one replace on a failed guess or a lockout reset | `/id` | every operation is a point read or a point write; there is no query on this container at all |
| `catalogue` | a filtered, sorted page, per request, in the experiment | seed of 100,000, once | `/make` | decided below |

The first four are the parity path and the key barely matters on it: nothing
queries `vehicles` or `photos` by a property, and `bids` and `users` are read by
key. The fifth is the experiment, and it is where the key is on the hot path.

## The candidates for the catalogue

The page filters on make, body style, title status, province, a price range and
a condition grade, searches free text, sorts four ways, and pages. The seed's
distributions, over 200 records, expand unchanged to 100,000 because
`SyntheticVehicleSource` varies numbers and never the make, style, status or
province of a seed record:

| path | distinct values | largest share | largest logical partition at 100,000 |
| --- | --- | --- | --- |
| `make` | 15 | Mazda and Ram, 18 of 200, 9 per cent | about 9,000 documents, about 7 MB |
| `body_style` | 5 | SUV, 106 of 200, 53 per cent | about 53,000 documents, about 43 MB |
| `province` | 7 | Ontario, 87 of 200, 44 per cent | about 43,500 documents, about 35 MB |
| `title_status` | 3 | clean, 170 of 200, 85 per cent | about 85,000 documents |
| `id` | 100,000 | one document each | one document |

**`/body_style`, `/province` and `/title_status` are rejected by their largest
share.** A key whose most common value holds half the data is a hot partition by
construction: half of every seed, and half of every read that names that value,
lands on one logical partition, and a logical partition can never use more
throughput than the physical partition it sits on. None of them is near the 20 GB
cap at this size, so the cost today would be throughput rather than storage, and
it would grow with the data rather than with the traffic.

**`/id` is the perfect spread and the wrong answer for a catalogue.** Every
document is its own partition, so a point read is always available and no
partition can be hot. But every filtered query is a cross-partition query,
because no filter names an id, and a catalogue is read by filters. It is the
right key for `users`, which is only ever read by id, and the wrong one here.

**`/make` is chosen.** Fifteen values with the largest at nine per cent is a flat
enough spread that no partition is hot, the largest logical partition at 100,000
documents is about 7 MB against a 20 GB cap, and make is the first filter on the
page and the one a visitor to a used-vehicle auction reaches for first. A query
with a make runs inside one logical partition; the ones without a make are the
cross-partition examples the interview question is about, and they are named
below rather than discovered later.

Two further shapes were considered and are recorded because they are the
answer to "what would you do at ten times the size":

- **A hierarchical key, `/make` then `/body_style`.** Supported by the service
  and the SDK. It sub-divides each make so a query naming both is narrower
  still, and it is the move when a single make outgrows a physical partition. At
  7 MB a make it buys nothing measurable, and it costs every query that names
  only the make a wider fan-out than it has today.
- **A synthetic key, a hash of the id into N buckets.** The write-heavy answer:
  it spreads a seed perfectly and makes every read cross-partition. This
  container is written once and read by filter, which is the case it is worst
  for.

## What the key costs

Two things the choice takes away, said plainly so the measurements below mean
something:

**A vehicle page does not know the make.** `/vehicles/{id}` carries only the id,
so in the experiment a read by id without the make is a query, not a point read:
`SELECT * FROM c WHERE c.id = @id` across every partition. The fix, if the
experiment ever became the site, is to carry the make in the address or to give
the synthetic ids a prefix the key can be derived from. On the parity path this
does not arise, because by-id reads come from the in-memory dictionary.

**The default view is a cross-partition query.** The front page with no filter,
sorted by ending soonest, names no make. Every page of it fans out. That is not
a defect of the key, it is what an unfiltered listing over a partitioned store
is, and the number for it belongs in the table below rather than in an
adjective.

## The caveat that keeps the numbers honest

At 82 MB and 1000 RU/s this container has one physical partition. A
cross-partition query therefore fans out to one place, and what the numbers
below measure is the cost of the index lookup and the documents loaded, not the
network cost of gathering answers from several servers. The Admin tab's store
log reads the physical partition count for each container through the SDK's feed
ranges and prints it beside every cross-partition operation, so the page says
"cross-partition, 1 physical partition" rather than implying a fan-out that is
not happening. The fan-out becomes real at 50 GB of data or 10,000 RU/s of
throughput, whichever arrives first, and the teaching record says what changes
then.

## The numbers

Measured on 2026-09-08 against the real account, the `catalogue` container
holding 100,000 documents on one physical partition. Two views of the same
seven queries: from the container itself, in region, which is what the Admin
tab's card shows, and from Steve's machine in Missouri through
`api/TheYard.Experiment`, ten paired rounds, median of each, where the
network is most of every millisecond and the request charge is the same
either way because the charge is the store's and not the network's.

| query on `catalogue`, 100,000 documents | partitions | RU | ms in region | ms from Missouri | documents |
| --- | --- | --- | --- | --- | --- |
| point read: id and make known | 1 logical | 1.00 | 6 | 58 | 1 |
| `make = 'Ford'`, by price, page of 100 | 1 logical | 5.86 | 19 | 192 | 100 |
| `province = 'Ontario'`, by price, page of 100 | all (1 physical) | 5.98 | 41 | 246 | 100 |
| no filter, by price, page of 100 | all (1 physical) | 5.70 | 14 | 246 | 100 |
| SUV, clean title, grade 4 or better, page of 100 | all (1 physical) | 6.53 | 13 | 234 | 100 |
| count of Ford (an aggregate inside one partition) | 1 logical | 3.00 | 11 | 60 | 8,000 |
| `id = @id`, make unknown | all (1 physical) | 2.83 | 4 | 59 | 1 |
| free text, `CONTAINS` on the model | all (1 physical) | 8.12 | 15 | 230 | 100 |

The same seven queries as the Admin tab's card shows them, run by the
container itself a few minutes later, warm:

![The partition key, live: the seven queries with their partitions, request charge, time and document count](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cosmos-cosmos-experiment.png)

Three things to read out of that.

**The point read is the cheapest thing in the table by a factor of three
against the same document fetched by a query that does not know the make**:
1.00 RU against 2.83. That is the cost of a partition key the address does not
carry, and it is the number behind the trade-off named above. On the parity
path it never arises; on a site that served pages from this container it
would be paid on every vehicle page.

**A page of a hundred documents costs about six request units whether the
query names the make or not.** Pinned and fanned out are within a request unit
of each other, and the record says why before the table does: with 82 MB on
one physical partition, a cross-partition query fans out to one place, and the
charge is the index lookup and the hundred documents loaded, not a gathering
from several servers. The wall-clock difference in region, 19 ms pinned against
41 ms for the province query, is the query engine planning and merging a
fan-out that has nowhere to go. This is the honest shape of the partition key
question at this size, and the teaching record says what changes at fifty
gigabytes or ten thousand request units a second.

**The free-text scan is the dearest query and still cheap**, 8.12 RU, because
`model` is one of the nine indexed paths and `CONTAINS` can use it. The
in-memory index the site actually uses answers the same question in under a
millisecond (ADR: The search index), and that comparison is the one that
decides where a search should run.

And the seed, both ways, which is the headline:

| seed of 100,000 documents | RU | RU per document | minutes | documents per second | failed |
| --- | --- | --- | --- | --- | --- |
| tuned policy, nine paths | 884,479 | 8.84 | 12.8 | 130 | 0 |
| default policy, every path | 1,471,488 for 91,593 | 16.07 | 21.2 | 72 | 8,407 |

The default policy costs 1.8 times as much per write and buys nothing on any
of the seven queries: the same set against `catalogue-default` came back
within half a request unit of the tuned container on every row (6.26 against
5.86 for the pinned page, 6.39 against 5.98 for the province page). The seed
against the default policy also did not finish: 8,407 documents were refused
after the SDK's thirty retries over two minutes on a 1000 RU/s database that
was being asked for twice that, which is what throttling looks like from the
client, and the record keeps the number rather than re-running it quietly.

Both seeds ran faster than the throughput arithmetic allows. 884,479 RU at
1000 RU/s is 14.7 minutes and the tuned seed took 12.8; the service lets an
idle database accumulate burst capacity and spend it, which is worth knowing
before reading any short measurement against a provisioned account.

## Files

- [`infra/cosmos/`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): one definition per container, with the key path and the indexing policy, which a person applies and the application maps to.
- [`api/TheYard.Infrastructure.Cosmos/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Infrastructure.Cosmos): the documents, the container catalog that declares the same keys, and the adapters.
- [`api/TheYard.Infrastructure/SyntheticVehicleSource.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/SyntheticVehicleSource.cs): why the seed's distributions are the catalogue's distributions.
- [`docs/ADR-059-a-second-store-priced.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-059-a-second-store-priced.md): the parent record, with the account and the arithmetic.
