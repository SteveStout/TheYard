# ADR: Site activity, and the line an address does not cross

Status: accepted, 2026-09-13, shipped as 1.0.0.114. Steve's ask: "in the admin
section I want an overall site activity by IP address and if it was cosmos or
sql and a graph showing site activity at the top, but we cannot display user
emails as that is private information."

## Context

The Admin tab could say what the container had served in the last few hundred
requests, from a ring of five hundred entries that empties on every roll
(ADR: Observability). It could not say what the site had served this week, or
who had been by, or which of the two stores each visitor landed on. The
telemetry that outlives the container is Application Insights on the free
tier, thirty days at a tenth of a gigabyte a day (ADR: Telemetry that
outlives the container), and it does not know which store served a request,
because the store is decided by a header the request carries and the
telemetry never asked.

Two things shaped the answer before a line was written.

**The Admin tab is public.** `GET /api/admin/metrics` answers 200 to anybody,
and that is by design: the page is the running system reporting on itself,
and the reasoning is on the Best Practices page. So "site activity by
visitor" is not a private log the owner reads. It is a page any stranger can
read, and a network range beside a timestamp on a public page is often enough
to name an employer, which is a thing a recruiter reading a portfolio at nine
at night did not sign up for. Truncating the address does not fix that; it
only shortens the sentence.

**The email rule is absolute.** No email address appears anywhere in this
feature: not in a response, not on the page, not in a tooltip, not in a log
line. A rule like that is not kept by remembering it. It is kept by a shape
that has no room for the thing.

## Decision

**The row is written by the store that served the request.** Two adapters
behind one port, the same shape as bids: two counter tables on the relational
side, two kinds of counter document in one container on the document side.
The store split the ask wants comes free, because the row lives where the
request was served, and the feature demonstrates the thing the site is about:
the same shape on two engines, side by side, read back from both onto one
graph.

Application Insights was the other candidate: durable, already paid for, no
new write path. It lost on the split and on the tests. The store does not
appear in its request telemetry, adding it means a telemetry initializer and
a query per Admin load through a managed identity, and none of it runs on a
developer's machine or in the gate, where the card would only ever show its
"not configured" state. A feature the gate cannot exercise is a feature the
gate cannot hold.

**Nothing waits on the store.** A request offers its hit to a bounded channel
and leaves; a hosted service drains the channel every five seconds, or sooner
when five hundred are waiting, folds the batch into one delta per store and
hour and one per store, day and visitor, and hands each store its own. A full
channel drops the oldest hit rather than blocking a request, and says so on
the card. The Performance page's numbers are the reason: a synchronous write
on every request would tax exactly the milliseconds that page is about.

**The counters add; they do not overwrite.** Two containers write the same
Azure SQL rows and the same Cosmos DB documents, the SQL site's and the
document site's. On SQL Server the counts move by `ExecuteUpdate`, so the
increment happens in the database; on Cosmos DB by a partial update whose
`Increment` the service applies. The one field that is read, merged and
written back is the JSON object of the top twenty paths, which a batch can
lose to a batch from the other container in the same five seconds. A count
of which paths were popular is worth exactly that much, and the trade is
recorded here rather than hidden.

**The activity writes are not on the Admin tab's other cards.** The relational
adapter uses a context factory without the SQL log interceptor and the
document adapter calls the container directly rather than through the
measured helpers, on purpose: a batch every five seconds would fill the
two-hundred-slot SQL log and store log with the feature that reads them, the
observer effect the request ring already had to design out. What the feature
costs is counted by the adapters instead and reported with the card.

### What a hit carries, and what it cannot

```live path=api/TheYard.Application/Activity.cs region=activity-port
```

Six fields. When; a visitor token; the network; the path; the store; whether
it looked like a bot. No user agent, no account, no email, no query string,
no full address. A type with no field for a thing cannot carry it, and the
tests assert the consequence on the wire rather than in prose: after requests
that put an address in the path and in the query string, neither response
contains an at sign.

**The visitor token** is a keyed hash of the day and the address, keyed with
the signing key every container shares and nobody outside has. Within a day
the same address is the same token on both sites, so a visitor's requests
group; tomorrow it is a different token, so nothing joins across days; and
the key is what stops anybody turning a token back into an address by
hashing the whole IPv4 space, which without a key takes an afternoon.

**The network** is the address cut to its first three octets, `203.0.113.x`,
which is enough to see a network, a country and an obvious scanner range and
not enough to name a machine. **A full address is never stored, on either
store.** That is the default and it stands until the owner rules otherwise in
writing; the record will say so here if he does.

```live path=api/TheYard.Api/Activity.cs region=visitor-token
```

**A path with an at sign in it** is recorded with the sign percent-encoded,
so an address pasted into a URL, which scanners do, cannot travel. The query
string is never recorded at all.

### Two endpoints, not one

**`GET /api/admin/activity`** is public, like the rest of the tab. It carries
the series per store on a fixed grid of buckets with zeros where nothing
happened, so the two lines share an axis; the totals; the split between what
looked like people and what looked like scanners; the top paths; which stores
keep activity here and why not; and what the collector has done. It names
nobody, and a test holds it to that.

**`GET /api/admin/activity/visitors`** is the one Admin read that is not
public. It answers only to the operator's key, presented as a header or a
query parameter and compared in constant time, and a wrong key, a missing key
and an unconfigured key are all a 404, so a stranger cannot tell the endpoint
exists. The key is `Admin__Key` in the container, filled at roll time from a
repository secret like the signing key; until the secret exists, the endpoint
does not. The page fetches the rows only when the address bar carries the key
and sends it in a header, so without the key the table does not exist in the
page any more than it exists on the wire.

The decision on that split was put to Steve with the two options priced:
rows behind a key only he holds, or aggregate only and no rows. He was away;
the key-protected shape is the default because it exposes nothing and keeps
the view he asked for, and he can rule the other way by deleting one endpoint.

```live path=api/TheYard.Api/Activity.cs region=report
```

### The graph

An inline SVG in the palette, the same two colours the drawing on the SQL
Server and Cosmos DB page uses for its numbers: the accent for the relational
store, the taupe deepened for the document store, both held to 3:1 on white
and on the page ground by the palette test, because a line is a graphic and
that is the floor for one. No chart library: the drawings under `docs/images`
are hand-drawn SVG and this is drawn the same way, with the geometry in a
module a unit test can hold. Three windows, the last day by the hour, the last
week by six hours, the last month by the day.

```live path=src/lib/activity.ts region=chart-geometry
```

### What the feature costs the stores

Measured on the gate's Cosmos DB pass: the collector's own counters and the
document adapter's request-unit total are on the card, so the number a
reader sees is the number the container paid, not a number written here that
goes stale. A hit is one partial update per hour and one per visitor per
batch, on the free tier's shared thousand request units a second, and the
container's documents expire after thirty-five days so it cleans itself.

The effect on the proof card, the paired rounds that the Performance page
quotes, is the measurement this record owes: the run before this change on
the second container read 41 ms and 2 ms round trips, and the run after it
ships is recorded in the addendum below when it has been read off the live
site, not before.

## Alternatives

**Application Insights, queried per Admin load.** Above: no store split
without a change to what is sent, and nothing to hold in the gate.

**Full addresses, behind the key.** Rejected by default and reversible in
writing. The data is written before it is read, so a decision to store the
address is a decision about every row from then on, and the operator who
wants it should say so where the record can quote him.

**The rows on the public endpoint, truncated.** Rejected. A `/24` and a
timestamp on a public page can name an employer, and the tab is public by
design (ADR: The one write a stranger can make).

**Storing the user agent.** It would sharpen the bot guess. It would also be
a fingerprint beside a network and a time, on rows an operator's key opens,
and a guess that is right often enough does not need it.

## Consequences

- The Admin tab opens on the graph and the two stores show against each
  other over a day, a week or a month, from durable rows rather than a ring
  that dies with the container.
- Two tables to publish to Azure SQL and one container to apply on Cosmos DB,
  by a person, like every other schema change here (ADR: Data first, and the
  database in source control). Until they are, each store's card line says
  what is missing and keeps nothing.
- One secret to add for the visitor rows to exist on the live sites.
- The test count moves, and so does the number on the resume that quotes it.

## Files

- [`api/TheYard.Application/Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Activity.cs): the port, the hit, and the folding of a batch into deltas.
- [`api/TheYard.Api/Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Activity.cs): the token, the bot guess, the collector, the report and the key.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the hook beside the request ring, the wiring, the two endpoints.
- [`api/TheYard.Infrastructure/EfActivityStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfActivityStore.cs) and [`ActivityRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/ActivityRows.cs): the relational adapter and its two rows.
- [`api/TheYard.Database/Tables/ActivityHours.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/ActivityHours.sql) and [`ActivityVisitors.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/ActivityVisitors.sql): the schema, which is the authority.
- [`api/TheYard.Infrastructure.Cosmos/CosmosActivityStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosActivityStore.cs) and [`infra/cosmos/activity.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/activity.json): the document adapter and the container it needs.
- [`src/lib/activity.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/activity.ts) and [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the geometry and the card.
- [`api/TheYard.Tests/ActivityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ActivityTests.cs) and [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): the folding, the token, the endpoints, and the at sign that is never there.

```live path=api/TheYard.Api/Program.cs region=activity-hook
```

```live path=api/TheYard.Api/Program.cs region=activity-endpoints
```

## Addendum, 2026-09-13: the measurement, read off both live containers on 1.0.0.114

The paired-round proof was run on both containers within a quarter of an
hour of the roll, each started by a throwaway account made for it, with the
activity collector writing to both stores the whole time. Read back from the
cards:

- **The live site's container:** 39 ms to Azure SQL Database and 2 ms to
  Azure Cosmos DB. "On 3 of 8 paths the two stores answer in the same time;
  4 more differ by exactly the round trip to the store." Bid write 84 ms
  against 13, bid raise 87 against 11, sign in 128 against 83, the vehicle
  page 1 against 1.
- **The second container:** 38 ms and 2 ms, the same sentence. Bid write 84
  against 11, bid raise 83 against 11.

Before the change, the second container's card from 1.0.0.112 read 41 ms and
2 ms with the same sentence, and the 1.0.0.94 run the Performance page quotes
read 39 ms and 2 ms with bid write at 84 against 10 and raise at 85 against
9. Every row that differs does so by a few milliseconds in both directions,
which is the noise the record on the proof already describes. The batch
writer off the request path costs the request path nothing the card can see.

What the feature itself cost during that read: the collector on the live
site's container had offered 88 hits and written 77 with no failed batch a
minute after the run; the second container 254 offered, 121 written, none
failed. Both containers then read the same rows back, 127 on each store from
either address, which is the row living where the request was served rather
than in the container that happened to answer.

Still owed and named here: the visitor rows answer 404 on both sites until
the repository secret `ADMIN_KEY` exists. The graph does not need it.

