# ADR: Same performance, proven

Status: accepted, 2026-09-09. A card on the Admin tab runs the same requests
a visitor makes against both stores, inside one container, in paired rounds,
and says on each path whether the two stores answer in the same time, and
when they do not, whether the difference is the stores or their distance.
Parent: ADR: One container, both stores.

## Context

Steve's ask, in his words: "prove SQL AND Cosmos DB have the same
performance". The measurement record (ADR: Measuring both stores) had
already compared the two containers from a laptop in Missouri, twenty
paired rounds, and found the pages that never touch a store equal to within
a few milliseconds and every write path faster on Cosmos DB by about eighty.
It also said why: the Cosmos DB account is in the container's region and the
SQL server is one region away, because that region refused to create one.

A number measured from a laptop across two containers is a number with two
networks in it. To prove the stores the same, the proof has to take the
networks out: the same process, the same request, the same request ring, and
only the store different. That is what one container running both stores
makes possible, and this record is the measurement that runs there.

## Decision

**The container measures itself.** `POST /api/admin/proof` starts a run in
the background; `GET /api/admin/proof` reads the run in progress or the last
result. A run registers one throwaway account per store, then for each of
eight rounds, alternating which store goes first, sends the requests a
visitor makes to this container's own address with the store named in a
header: sign in, the listing page, a vehicle page, the filter values, a bid,
a raise, and a reset. Every request is timed around the whole exchange,
body included, and the two rings are read for what it caused: how many
statements or operations, and what the document store charged.

```live path=api/TheYard.Api/Proof.cs region=rounds
```

**The round trip to each store is measured on its own.** After the rounds,
five round trips to each store doing as little as a round trip can: a
`SELECT 1` on the relational side, a point read of one document on the
document side, the median kept. That number is what lets the card say
whether a difference is the store or the distance to it.

```live path=api/TheYard.Api/Proof.cs region=timed
```

**The verdict is arithmetic, not adjectives.** Per path: the median of each
store's samples, and the median of the paired differences, round by round,
so a slow second on both sides cancels. Two stores are "the same" on a path
when that difference is within fifteen milliseconds or fifteen per cent of
the slower one, whichever is more; fifteen milliseconds is about the spread
between two rounds of the same request on the same store. When they are
not the same, the card names the leader and by how much, and then takes one
round trip per operation off each side: if what is left is within the
tolerance, the card says the difference is all round trip.

```live path=api/TheYard.Api/Proof.cs region=verdict
```

**The card says the whole thing in one sentence**, computed from the rows:
how many paths are the same, how many differ by exactly the round trip, and
how many differ by more, with the round trips stated.

**A run is public and rationed.** The endpoint that starts a run is as
public as the rest of the Admin tab, and a run registers two accounts and
places sixteen bids, so a second start while one is running, or within a
minute of the last, answers 409 and the card shows the result it has. The
site's hourly allowance of registrations (ADR: The one write a stranger can
make) is the outer limit.

## What it measures, and what it does not

It measures the visitor's path: HTTP in, JSON out, with authentication,
validation, the domain rules, the store and the serialisation all inside the
clock, the way a visitor's request has all of them inside it. It does not
measure the visitor's own network, which the measurement record does, and it
does not measure the stores under load, which nothing here does: one request
at a time, in order, is the shape of a demo with one visitor and it is the
shape this proof is honest about.

The two rings the proof reads for statements, operations and request charge
are the container's, so a visitor bidding during a run adds their statements
to a sample's count on whichever store served them. The times are the
proof's own requests and unaffected; the operation counts, and with them the
"without the round trips" column, can carry a visitor's work for the few
seconds a run takes. Reading the card a second time settles it.

The relational round trip is a whole statement, not the wire alone, so the
"without the round trips" column on a path with several statements takes off
a little more than the wire on that side. The direction of the error is
towards the relational store, which is the store the distance handicaps, so
the correction can only understate that store's case, never overstate it.

## The numbers

Run on both containers on 1.0.0.94 within a quarter of an hour of each coming
up, eight paired rounds each time, read back from the live sites. The
sentence both cards arrived at, word for word (the second container's round
trip to Cosmos DB came out at 1 ms rather than 2):

> On 3 of 8 paths the two stores answer in the same time. On the other 5 the
> difference is the round trip to the store, 39 ms to Azure SQL Database and
> 2 ms to Azure Cosmos DB, and taking one round trip per operation off each
> side leaves them the same.

That is the proof, and it is the honest one: the two stores answer in the
same time once the distance to each is taken out, and the distance is real.
The relational server is one region away from the container and every
statement crosses that gap; the document account is in the container's own
region. On this workload a bid is two statements or two operations, so the
relational side pays the gap twice and the card shows exactly that.

The rows from the live site's container. Times are the median over eight
samples unless marked; the difference is the median of the paired
differences, negative when Cosmos DB was faster, which is why it is not
always the difference of the two medians; the last column takes one round
trip per statement or operation off each side:

| Path | Azure SQL Database | Azure Cosmos DB | Difference | Without the round trips | Verdict |
| --- | --- | --- | --- | --- | --- |
| Register (one sample) | 233 ms, 3 statements | 133 ms, 4 operations, 13.04 RU | -100 ms | +9 ms | all of it the round trip |
| Sign in | 122 ms, 1 statement | 82 ms, 2 operations, 2 RU | -39 ms | -4 ms | all of it the round trip |
| Listing page | 51 ms | 56 ms | -6 ms | -6 ms | the same |
| Vehicle page | 1 ms | 1 ms | 0 ms | 0 ms | the same |
| Filter values | 12 ms | 29 ms | +7 ms | +7 ms | the same |
| Bid write | 84 ms, 2 statements | 10 ms, 2 operations, 6.52 RU | -75 ms | -1 ms | all of it the round trip |
| Bid raise | 85 ms, 2 statements | 9 ms, 2 operations, 11.29 RU | -77 ms | -3 ms | all of it the round trip |
| Reset | 44 ms, 1 statement | 10 ms, 2 operations, 7.78 RU | -35 ms | 0 ms | all of it the round trip |

The second container, whose default is the document store, produced the same
shape to within a few milliseconds on every row: register 197 against 94 with
10 ms left after the round trips, sign in 117 against 77 with 1 ms left, bid
write 85 against 11 with 2 ms left, bid raise 86 against 10 with 1 ms left,
reset 44 against 10 with 3 ms left, and the three pages that never touch a
store the same on both. Two containers, two runs each, one answer.

The card on the live site after that run, and the same card on the second
container:

![The proof card on the live site: the sentence, the round trips, and the eight rows with both stores, the difference, the difference without the round trips, and a verdict on each](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/proof-sql.png)

![The proof card on the second container, whose default store is Cosmos DB: the same sentence and the same shape on every row](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/proof-cosmos.png)

Two things the run taught that the card alone would not.

**The first run after a deploy is not the second.** On both containers the
first run since boot put Register at 578 ms against 194 on the live site and
483 against 163 on the second, with 275 and 207 ms left after the round
trips, and the verdict said so. The second run a few minutes later put the
same row at 233 against 133 and 197 against 94, with 9 and 10 ms left. The
card registers one account per store per run, so that row has one sample and
carries whatever a process does only once: the first pass through a code
path on both sides, and on the relational side the compiling EF Core does the
first time it meets a query shape and a command shape. This record does not
prove the cause, only that a second run removes it, and it keeps both numbers
because the first is the one a visitor gets right after a deploy.

**The relational store's free offer sleeps.** The first health check after a
quiet stretch took 25,882 ms on the live site and a direct read of the origin
timed out, because Azure SQL Database's free offer pauses the database when
nobody has used it for a while and resumes it on the next connection. The
script that started these runs wakes both stores with a health check first
and starts a run only when every store answers in under a second and a half,
so no row here is a store waking up; a visitor who arrives after a pause pays
it once, on the first request that reaches the database, and the site serves
the catalogue from memory while it waits. The document store has no such
state on the free tier.

## Consequences

- The proof is a card, so it runs on the deployed container and not on a
  laptop, and the numbers it shows are the numbers a visitor to that
  container gets, minus their own network.
- Two throwaway accounts per run, named `proof-...@example.com`, live in each
  store until the next reset of that store's test data; on the live stores
  they accumulate at two per run, which the cooldown bounds at a hundred and
  twenty a day and the registration allowance bounds harder.
- The interview sentence is the card's sentence, and the numbers behind it
  are on the page it is said from.

## Files

- [`api/TheYard.Api/Proof.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Proof.cs): the runner, the samples, the verdict.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the two endpoints, the loopback address, the client the proof uses.
- [`api/TheYard.Tests/ProofTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ProofTests.cs): the arithmetic without a store, and the endpoints with whichever stores the run has.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card.
- [`docs/ADR-064-measuring-both-stores.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-064-measuring-both-stores.md): the measurement from the visitor's side, which this one completes.
