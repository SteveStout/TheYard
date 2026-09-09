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

The relational round trip is a whole statement, not the wire alone, so the
"without the round trips" column on a path with several statements takes off
a little more than the wire on that side. The direction of the error is
towards the relational store, which is the store the distance handicaps, so
the correction can only understate that store's case, never overstate it.

## The numbers

Filled in from the live site after this shipped; the card on either site is
the current version of them.

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
