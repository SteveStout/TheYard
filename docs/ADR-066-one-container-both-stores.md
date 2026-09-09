# ADR: One container, both stores

Status: accepted, 2026-09-08. One container runs the relational store and the
document store side by side, a visitor picks one with a toggle at the top of
every page, and the same request is served by whichever store the visitor
chose. The second container stays, running the same image with the other
store as its default. Parent: ADR: A second store on Cosmos DB, and what it
costs.

## Context

Two containers, two tabs, and a card that read the other container through
its API (ADR: Backends, side by side) answered the first question: can the
same application run on a document store. Steve's next two asks changed the
shape. He wanted "an easy way to toggle between Cosmos and SQL at the top of
the page", and he wanted the two stores' performance proven the same.

A toggle that opens the other site is a link, and the other site has a plain
http address on an Azure hostname, so the demo changes address bars every
click. A proof measured across two containers measures two containers: the
network between them and the visitor, two processes with two request rings,
two cold starts on two machines. What both asks want is one process, one
address, one request, and only the store different.

## Decision

**A container runs every store it is configured for.** The relational store
is always there, SQL Server when the deploy gave one and SQLite otherwise; the
document store is there when `Cosmos:AccountEndpoint` is set. Each is brought
up on its own, timed on its own, and stands behind its own catalogue, bids,
room and accounts. Nothing is shared between them, and that is the point: a
listing served by the Cosmos DB backend was loaded from Cosmos DB, and the
cold start beside it is that store's own.

```live path=api/TheYard.Api/Stores.cs region=backend
```

**Which store serves a request is decided per request.** As first shipped,
the toggle set a cookie for a year and the header won over the cookie over
the container's default; since 1.0.0.101 there is no cookie (the addendum
"the toggle moved to the sites" says why), so it is the header, for a
measurement that has no browser, and otherwise the container's default,
which the deploy names (`Store:Default`, `sql` on the live site and `cosmos`
on the second container). A value that names a store this container does not
have is the default and never an error.

```live path=api/TheYard.Api/Stores.cs region=backends
```

**Endpoints ask the request, not the container.** Every endpoint that used to
take `InventoryService`, `BidService` and `MarketService` from the container
takes `CurrentBackend`, which is scoped to the request and resolved once from
its headers and cookies. The three services are no longer registered at all:
a singleton of any of them would be a singleton of one store. Identity gets
the same treatment one level down: `UserManager` is registered once, and the
store behind it is chosen per request, Identity's own tables on the
relational backend and one document per account on the document one.

```live path=api/TheYard.Api/Program.cs region=user-store-per-request
```

**The toggle is a cookie and a reload.** `POST /api/stores/select` sets the
cookie and answers what `GET /api/stores` answers, which stores there are and
which one this request is on; the page then reloads itself, because every
number it holds was read from the other store and a cache of the wrong
store's answers is worse than a cold page. The toggle always shows both
families, so the choice reads as a choice: a family this container does not
run is drawn as not here, and a store that did not come up cannot be chosen
and says why. The server refuses the same choice with the same sentence
(`Backends.Choose`), so a stale page or a script cannot set a year-long
cookie for a store with no accounts behind it.

```live path=src/lib/stores.ts region=stores-seam
```

**Accounts live in the store they were made in.** An account is a row in one
store or a document in the other, so a session opened on Cosmos DB reads as
signed out on Azure SQL and signs back in when the toggle goes back. The bar
says so beside the toggle rather than pretending the two stores are one.

**The comparison card compares the stores in this container.** A container
running both puts them on the same rows it used to put two containers on, the
one serving this visit first, and says that the two columns share a process,
a region and a request ring. A container running one still compares itself
with its peer, so the second container and its card keep working unchanged.
The request ring records which store served each request, which is what lets
one ring be split two ways.

```live path=api/TheYard.Api/Program.cs region=backends-metrics
```

**The health check names every store.** One check per store, the default's
still called `database` for the deploy's Verify step and the Admin tab's card,
the other named by its key. Neither gates readiness, for the reason the
relational store record gives: a container with no database still serves the
catalogue, and the toggle shows that store as unavailable rather than the
site as down.

## What it looks like

The live site on 1.0.0.94, arriving on its default store. The bar sits above
the view on every page and says which store served it:

![The Store bar at the top of the live site: SQL selected, Cosmos DB beside it, and the sentence that this page is served from Azure SQL Database and that accounts and bids live in the store they were made in](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-sql-top.png)

The same address after one click on Cosmos DB. The page reloaded itself on
the other store, `/api/stores` answered `cosmos` for the page's own request,
and the sentence changed with it:

![The same page after the toggle: Cosmos DB selected, and the sentence now says the page is served from Azure Cosmos DB](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-sql-switched.png)

The second container arrives on the other segment and switches the other
way; its screenshot on arrival is the switched picture above, byte for byte,
because both containers run the same image against the same two stores. On a
phone the bar sits under the header and the sentence wraps beneath the
segments:

![The bar on a phone: the Store segments under the header, the sentence wrapped beneath them](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-phone.png)

And the comparison card on a container running both, which is where the
proof record's numbers come from: two stores in one process, one region and
one request ring, so what differs between the columns is the store and the
distance to it:

![Backends, side by side, on a container running both stores: cold start, store check, seed and catalogue load per store, then the visitor's paths with the request charge beside the document store's numbers, and the line that both stores run in this container](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/backends-one-process.png)

## What it costs

Two catalogues. Each store expands its two hundred seed vehicles to a hundred
thousand in memory, so a container running both holds two of everything the
inventory service holds. The cold start is not longer for it: the default
store is warmed before the container serves anything, as it always was, and
the other is warmed in the background right after, one store at a time,
because two expansions racing each other on one vCPU would both take longer
and neither number would be that store's own. That background warm-up is a
deploy setting (`Store:WarmOthers`) and is off everywhere else, because a
test run boots ten applications at once and ten second expansions nobody
asked for is memory the machine running the suite does not have to give; a
store nobody warmed warms itself on its first request, which is the Lazy the
inventory service has always had (ADR: The ports learn to wait).

Two more settings on each container group: the other store's, and the
default. The live site's group gains the Cosmos DB endpoint, the database name
and the credential kind, none of them a secret; the second container's group
gains the SQL Server connection string the deploy already composes for the
first, substituted by the same step. Both groups run the same image and differ
in three environment variables.

## What this is not

**Not a shared catalogue.** The two backends hold identical vehicles, because
both stores were seeded from the same file, and sharing the expansion would
halve the memory. It would also make the listing on the Cosmos DB side a
listing served from SQL Server's load, and the comparison would be comparing
one thing with itself.

**Not a migration path.** Nothing here moves a bid or an account from one
store to the other. A visitor who bids on one store and toggles sees no bids,
which is true, and the bar says why.

**Not a cluster.** Two containers now open the same two stores, and each
replays the bids into its own memory at startup, so a bid placed on one
container is not on the other's page until that container restarts. That is
the one-container assumption ADR: The one write a stranger can make already
states for the rate limit, and the second container inherits it; accounts,
which are read from the store on every request, are shared between the two
containers at once.

## Consequences

- The toggle at the top of every page switches the store under the same
  address, and the second container still exists for the two-tab comparison.
- The performance proof can be run inside one process, against both stores,
  from the same request ring: identical container, identical request, only the
  store differs (ADR: Same performance, proven).
- `Program.cs` grew by a hundred and forty lines and lost three registrations;
  ADR: Program.cs, explained carries an addendum on the new shape.
- Every test that booted the application on Cosmos DB now boots it on both,
  with Cosmos DB as the default, which is the same suite exercising the
  selection as well.

## Addendum, 2026-09-09: the night's own code, reviewed

The second review of the day's code, the second pass in ADR: Reviewing my
own work, and what that found, covered the document store. This pass read
what the night added,
`Stores.cs`, `Proof.cs`, the bar and the card, after both had run live.

- The bar would not offer a store that did not come up, and the server took
  the choice anyway. A page loaded before a deploy, or a script, could set a
  year-long cookie for a store with no accounts behind it. The rule now lives
  beside the selection rule as `Backends.Choose`, the endpoint asks it, and a
  test walks a container whose document store did not come up.
- The bar's sentence said "served from Azure Cosmos DB" for a visitor whose
  cookie named a store that did not come up, which is true of the catalogue
  and false of everything else. It now says the store did not come up, that
  the catalogue is served from files, and that there are no accounts on it
  until it does (`note` in `src/lib/stores.ts`, with a test).
- The proof reads the container's two rings for what each of its requests
  caused, and the rings are the container's, so a visitor bidding during a
  run adds their statements to a sample's count. The times are the proof's
  own requests; the counts can carry a visitor's. The proof record says so
  now. Nothing else in `Proof.cs` needed to change: the pairing, the
  tolerance and the correction were read line by line against the numbers
  the live run produced and they agree.

## Addendum, 2026-09-09: the other site, on every page

Steve's answer to the priced question was the second option as well as the
first: a link between the two sites. The bar now carries it. `Peer:Site` is
the other container as a visitor reaches it, which for the live site is the
domain behind the edge and for the second container is its own origin, and
`/api/stores` answers it beside the stores as `other_site`, trimmed to an
http or https origin or left null. The bar shows the host as the link text,
after the sentence about the store serving the page, so both addresses are on
every page of both sites and a visitor on either can open the other. The
peer endpoint keeps reading the other container by its origin; the two
settings differ on the live site for the same reason a visitor and a
container reach it by different names.

The bar on the live site, and on the second container, on 1.0.0.98:

![The Store bar on the live site: SQL selected, and under the sentence a link to the other site, the second container's address](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-sql-linked.png)

![The Store bar on the second container: Cosmos DB selected, and a link to the other site, the domain](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-cosmos-linked.png)

Superseded the same morning by the addendum below, where the link becomes the
toggle itself.

## Addendum, 2026-09-09: the toggle moved to the sites

Steve's words on the morning after the night's work, having opened both
sites: "the button should toggle between the two sites, not refresh in React;
at the least, the URL should change". The in-place switch above was the first
of the two priced options and it worked, and it was not what he wanted: one
address serving either store, a cookie remembering which, and a page that
reloads itself to become the other store is a toggle a visitor cannot see in
the address bar and cannot send to anyone.

**The segments are the sites.** The Store bar still shows SQL and Cosmos DB
side by side. The segment for the site the visitor is on is marked current
and is not a control; the other is a link to the other site at the same path
and query, so `/?view=admin` on the live site lands on `/?view=admin` on the
second site. A full navigation: the address bar changes, nothing is cached
across a store, and the page that arrives is the other site's own, on its
default store. The other site's address is `other_site` from `/api/stores`,
which is the container's `Peer:Site`, and since ADR: A permanent address for
the second site both ends of the link are HTTPS. Where a container names no
other site, which is a developer's machine, CI and the ship gate, the other
segment is drawn as not here with a title that says so, the way a family the
container did not run was drawn before; never a dead button. The sentence
under the bar names the site, the store serving the page and where accounts
and bids live; the separate link under the sentence went, because the
segments are the links now.

```live path=src/lib/stores.ts region=stores-seam
```

**The cookie retired with the switch.** Each site is one store's site now, so
a store chosen by cookie would contradict the address bar, and the only thing
that ever set the cookie was the bar. Left in place it would have done real
harm to the one visitor most likely to carry it: anyone who toggled on
1.0.0.94 to 1.0.0.100, Steve included, would have landed on the live site
served from the document store with no control left to change it.
`Backends.For` is the header or the default; `GET /api/stores` expires the
old cookie the first time a request still carries one; `POST /api/stores/select`
is gone, and its tests went with it, replaced by one that holds the expiry
and the refusal (a 405, because the page's own fallback answers GET on every
path, so the path exists and the method does not). The header stays, because the proof (ADR: Same performance,
proven) and the measurements name a store for one request with it, which is
a different thing from pretending the site changed.

```live path=api/TheYard.Api/Stores.cs region=backends
```

**What holds it.** `src/lib/stores.test.ts` holds the segment shape: the
current site follows the container's default store and not the store a
header put one request on, the other segment's address carries the path and
the query, and a container with no other site gets a segment that is not a
link. `tests/e2e/store-toggle.spec.ts` walks the bar in a browser, where on
the gate the other segment is not a link and the old switch endpoint refuses
the method. `StoreToggleTests` holds the rule and the expiry. The one place the real
thing is proven is the live check, `shot-night.mjs` in the lane's notes
outside the repository,
which opens each site, follows the other segment, and reads the host and the
server's own answer on arrival, both ways.

**What the live check read on 1.0.0.101, at 08:47 CDT on 2026-09-09.** On
`theyard.stevenstout.biz` the bar's current segment was SQL and the other
segment's address was `https://theyard-cosmos.stevenstout.biz/`; following
it landed on host `theyard-cosmos.stevenstout.biz`, where the server answered
`current=cosmos` for a container whose default is `cosmos` and the bar's
current segment was Cosmos DB; following that bar's SQL segment came back to
`theyard.stevenstout.biz` on `sql`. The same round trip the other way from
the second site. On `/?view=admin` the other segment's address on the live
site was `https://theyard-cosmos.stevenstout.biz/?view=admin`, the path and
the query carried across. And the old switch endpoint answered 405 on both
sites.

The live site on arrival, and the page the Cosmos DB segment lands on, which
is the other site (the second site's own arrival picture and the page its SQL
segment lands on are these two the other way round, byte for byte, so they
are not repeated):

![The live site on 1.0.0.101: the Store bar with SQL current, Cosmos DB beside it, and the sentence that this is the SQL site served from Azure SQL Database](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-site-sql.png)

![After following the Cosmos DB segment: the second site, theyard-cosmos.stevenstout.biz, with Cosmos DB current and the sentence that this is the Cosmos DB site served from Azure Cosmos DB](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-site-sql-followed.png)

The two bars on their own, one from each site:

![The Store bar on the live site: SQL current, Cosmos DB a link](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-site-sql-bar.png)

![The Store bar on the second site: Cosmos DB current, SQL a link](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-site-cosmos-bar.png)

And on a phone, where the sentence wraps under the segments as before:

![The bar on a phone on 1.0.0.101](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/toggle-site-phone.png)

## Files

- [`api/TheYard.Api/Stores.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Stores.cs): a backend, the backends, the request's choice, and the context factory.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): two stores brought up, the endpoints on the request's store, the health check per store, the metrics per store, the toggle's endpoints.
- [`api/TheYard.Tests/StoreToggleTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StoreToggleTests.cs): the rule, the endpoints, the ring.
- [`src/lib/stores.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/stores.ts) and [`src/components/StoreBar.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/StoreBar.tsx): the toggle.
- [`tests/e2e/store-toggle.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/store-toggle.spec.ts): the toggle in a browser, on one store and on two.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the two groups, three variables apart.
