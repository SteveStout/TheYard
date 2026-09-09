# ADR: Backends, side by side

Status: accepted, 2026-09-08. One card at the top of the Admin tab, on both
sites, with this container and its peer on the same rows: the store, the cold
start, the seed and what it cost, and how long the things a visitor does take
on each, with the request charge beside every number the document store can put
one on. Parent: ADR: A second store on Cosmos DB, and what it costs.

## Context

Steve wanted the comparison built into the product rather than pasted into a
document, and he wanted the same Admin section on both sites. One component, one
image, deployed twice: whichever tab he opens, he sees both backends.

## Decision

**The peer is read through this container's API, not from the browser.**
`/api/admin/peer` fetches the peer's `/api/admin/metrics` server side and
returns it. That keeps CORS closed on both origins and keeps the peer's address
out of the client bundle: one configuration value per container, `Peer:Url`,
holds the peer's public origin, which is a URL and not a credential, and the
browser only ever learns the peer's host name.

```live path=api/TheYard.Api/Peer.cs region=peer
```

**The peer is a thing that can be down.** ADR: The relational store established
that the site comes up when the database does not, and the same rule holds
here: no peer configured, no answer, a slow answer and a wrong answer are four
different sentences on the card, and none of them is an exception. Two and a
half seconds of patience, not a hang, because the card refreshes every thirty
seconds and a peer that takes longer than that is down for the purposes of a
comparison. The rest of the Admin tab renders whatever the peer says.

The tests were written in the order the record asks for, the failures first:

```live path=api/TheYard.Tests/PeerTests.cs region=peer-down
```

and the happy path last, against a canned peer on the loopback:

```live path=api/TheYard.Tests/PeerTests.cs region=peer-up
```

**The rows are routes, not paths.** A bid on one vehicle and a bid on another
are the same operation, and a card that listed a hundred thousand paths would
say nothing. Timing is grouped by route template, with the identifiers taken
out, and the store log's operations are grouped the same way so the document
column can say what a route costs in request units as well as milliseconds:

```live path=api/TheYard.Api/Peer.cs region=routes
```

The request-unit figure per route is exact per request: every operation the
store log records carries the trace identifier ASP.NET Core gives the request
that caused it, so twenty sign-ins are twenty samples of 2 RU and not one sum
of 40. The first version of this card grouped by method and path instead and
reported twenty sign-ins as a single 34 RU request, which the first
measurement session caught (ADR: Measuring both stores).

**Cold start is measured on each container, at its own start.** How long the
store took to answer, how long the catalogue and the bids took to load, and
when the container was ready to serve, all counted from the process's own
start time and exposed in the metrics, so the two columns compare two real
cold starts rather than one stopwatch and one memory:

```live path=api/TheYard.Api/AdminObservability.cs region=startup-timings
```

**The card.** Two columns, this container first, the peer second, on rows for
the store, the cold start, the store check, the seed with its charge, the
catalogue and bid loads, the requests window, the seven routes a visitor
actually takes, and the store operations window:

```live path=src/components/AdminPanel.tsx region=comparison
```

## What it looks like

Both cards on 1.0.0.92, taken a minute apart after six paired rounds of the
measurement script had given every row on both sides something to show. The
live site first, reading its peer:

![Backends, side by side, on the Azure SQL site: this site's column first, the Cosmos DB peer second, with the request charge beside the peer's sign-in, register and bid rows](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cosmos-sql-backends.png)

And the same card on the Cosmos DB container, where the columns swap and the
request charge sits in the first column:

![Backends, side by side, on the Cosmos DB site: this site's column first with its request charges, the Azure SQL peer second](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/cosmos-cosmos-backends.png)

Read across any row and the two containers agree with each other about the
other one, which is the property the peer endpoint exists to give: each site
reads the other's metrics rather than remembering them. The bid write row is
the one to look at twice. Server side the document store spends 11 ms on a
bid and the relational one 86, and the measurement record shows what is left
of that difference by the time it reaches a visitor in Missouri
(ADR: Measuring both stores).

## Consequences

- Both sites carry the same card, and opening either shows both backends.
- The comparison is live, not a screenshot: every number is the last few
  hundred requests each container has seen, and the cold start is the one it
  actually had.
- `/api/admin/peer` and `/api/admin/store` are excluded from the timing rings
  like the other observability reads, or the card would fill with itself.
- A peer address is one more environment variable on each container group,
  set in the deploy, never in the repository.
- The one thing this card cannot do is compare on equal traffic. The live SQL
  site has visitors and the Cosmos one has whoever is comparing; the measurement
  record runs the same requests against both in one session for that reason
  (ADR: Measuring both stores).

## Addendum, 2026-09-08: the two columns moved into one process

The evening's ask was a toggle at the top of the page, and the answer to it
(ADR: One container, both stores) changed what this card compares. A
container that runs both stores now puts them on these same rows with each
other, the one serving the visit first, and says in a note that the two
columns share a process, a region and a request ring: only the store
differs. The request ring records which store served each request, so one
ring is split two ways rather than two rings being read from two places.

Nothing above is withdrawn. The peer endpoint, its patience and its four
sentences are what a container running one store still uses, and the second
container still runs the same image with the other default, so the two-tab
comparison this record was written for still works. What the in-process
comparison adds is the one thing the two-container one could not have: a
measurement with no network in it.

## Files

- [`api/TheYard.Api/Peer.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Peer.cs): the peer reader and the route grouping.
- [`api/TheYard.Api/AdminObservability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/AdminObservability.cs): the startup timings and the store window.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the endpoint and the metrics it relays.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card.
- [`api/TheYard.Tests/PeerTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/PeerTests.cs): down first, up last.
- [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the second container group, with its peer setting.
