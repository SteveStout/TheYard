# ADR: Site activity, and the line an address does not cross

Status: accepted, 2026-09-13, shipped as 1.0.0.114. Steve's ask: "in the admin
section I want an overall site activity by IP address and if it was cosmos or
sql and a graph showing site activity at the top, but we cannot display user
emails as that is private information."

## In plain words

The Admin tab now shows a graph of site activity, split by which database served each visit (Azure SQL or Azure Cosmos DB), kept in durable rows that survive a restart. No email address is ever recorded, and a visitor's address is kept only as a daily keyed hash plus its network (the first three octets).

What that is worth: a developer sees a privacy rule kept by a data shape that has no field for the private thing, and the organization can publish its traffic on a public page without exposing who visited.

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
to name an employer, which is a thing a visitor reading a portfolio at nine
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

```live path=api/TheYard.Api/ActivityReport.cs region=report
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

```live path=src/lib/activityChart.ts region=chart-geometry
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

## Where it sits

Site activity runs through five rings: ActivityHit and the IActivityStore port sit in Application, EfActivityStore in Infrastructure and CosmosActivityStore in Infrastructure.Cosmos implement that port, the collector and visitor token live in the host Api, and the chart geometry is React-free code in src/lib. That is dependency inversion, where the inner ring owns the interface and the outer rings fill it in, and the 14 September move to a single keeper leaned on Liskov substitution, since the Cosmos DB adapter took every row without the collector changing. It cost a second schema to publish by hand and a top-paths field that can lose a batch when both containers write in the same five seconds. Application Insights would be the better home if the serving store could travel as a custom dimension and the gate had no need to exercise the card.

## Files

- [`api/TheYard.Application/Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Activity.cs): the port, the hit, and the folding of a batch into deltas.
- [`api/TheYard.Api/Activity.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Activity.cs): the token, the bot guess, the collector and the report.
- [`api/TheYard.Api/AdminKey.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/AdminKey.cs): the key, read from the query or the header in one place and compared in constant time.
- [`api/TheYard.Api/Composition/ObservabilityRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ObservabilityRegistration.cs): the hook beside the request ring (region activity-hook).
- [`api/TheYard.Api/Composition/ActivityRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ActivityRegistration.cs): the wiring.
- [`api/TheYard.Api/Endpoints/AdminEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/AdminEndpoints.cs): the two endpoints (region activity-endpoints).
- [`api/TheYard.Infrastructure/EfActivityStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/EfActivityStore.cs) and [`ActivityRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure/ActivityRows.cs): the relational adapter and its two rows.
- [`api/TheYard.Database/Tables/ActivityHours.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/ActivityHours.sql) and [`ActivityVisitors.sql`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Database/Tables/ActivityVisitors.sql): the schema, which is the authority.
- [`api/TheYard.Infrastructure.Cosmos/CosmosActivityStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosActivityStore.cs) and [`infra/cosmos/activity.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/activity.json): the document adapter and the container it needs.
- [`src/lib/activity.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/activity.ts) and [`src/components/admin/ActivityCard/ActivityCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/ActivityCard/ActivityCard.tsx): the geometry and the card.
- [`api/TheYard.Tests/ActivityTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ActivityTests.cs) and [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): the folding, the token, the endpoints, and the at sign that is never there.

```live path=api/TheYard.Api/Composition/ObservabilityRegistration.cs region=activity-hook
```

```live path=api/TheYard.Api/Endpoints/AdminEndpoints.cs region=activity-endpoints
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

## Addendum, 2026-09-13: unique visitors per day, and the table grouped by day

Steve, on seeing the first version: "on the graph I want it per all unique
ips per day with a table below of each users interaction, grouped by day on
the user interaction, so I get a feel for how people are using it, and it
should be at the top."

The graph now draws unique visitors per UTC day: everybody as one line in the
heading colour, and one line per store under it in the store colours, on a
window of a week by default. A "unique visitor" is a distinct visitor token
for that day, which is a distinct address for that day, counted on the server
from the visitor rows and thrown away: the public endpoint carries the count
per day and per store and never a token. The requests-per-bucket series stays
on the wire for the tests that hold it and for anyone who wants the old view.

The table under the graph groups each visitor's day under a heading row for
the day, newest day first, with the day's own count of visitors and requests
in the heading; within a day the columns still sort either way. The rows are
the same rows as before and travel the same way, behind the operator's key,
which is the reading this addendum keeps until he says otherwise: the tab is
public, and a network beside a timestamp is the thing the record above
decided not to publish. The graph, the day counts and the split need no key.

```live path=src/lib/activityDays.ts region=days
```

## Addendum, 2026-09-13: the counters are kept for good

Steve, the same afternoon, after the kept log shipped: "But I want long term
logs. Of site activity for sure." The thirty-five days above were chosen to
match the graph's longest window and nothing else, and the documents are
small: one per store per hour and one per visitor per store per day, a few
hundred a day at today's traffic and a few tens of megabytes a year. So the
`activity` container's default time-to-live is now none (`-1` in the
definition, which keeps time-to-live enabled and expires nothing), applied to
the live account by `az cosmosdb sql container update` and read back, and the
relational side's two tables were never purged. The card says the retention
it reads from the container rather than a sentence written here. The graph's
windows stay at 24 hours, 7 days and 30 days: a longer window reads every
visitor document in it to count the day's unique tokens, and a year of those
is tens of thousands of reads per Admin load, which is a separate decision
with a separate cost if he wants one.

## Addendum, 2026-09-13: the browser remembers the key

Steve, from his phone, an hour after the retention change: "I can't see the
kept log on the public site, you should be able to see it there." Measured
before anything was changed: the runner opened the keyed URL in the repo's
own headless Chromium and the card rendered with 130 lines, and the same
page without the key showed the one-line note. The key was in a file on his
machine, and he was reading the site from his phone.

So the page now remembers the key. The address bar wins and is written to
this browser's local storage; a visit without it reads what the browser
kept; a button under the visitor table forgets it, at once and for the next
visit. The key still never leaves the browser except as the header the two
keyed endpoints read, the app still drops it from the address bar on the
first render, and a bookmark saved after the page loaded now works, which it
did not before. What this costs: anyone with that browser unlocked sees the
operator's cards, which is the same trust the browser's saved sessions
already carry, and the forget button is the answer on a shared machine. The
browser suite opens the keyed URL once, then the plain one, and holds both
the remembering and the forgetting.

## Addendum, 2026-09-13: the key can be typed in

Steve, from his phone, with a screenshot: the keyed URL opened the Admin
tab and the kept log card said the log answers only to the operator's key,
which means the page loaded with no `key=` in its address. The same URL in
an emulated iPhone from the runner rendered the card with two hundred rows,
so the page and the key were right and the link's journey to the phone was
not; how the query string was lost on the way is not measured and is not
named. What is fixed is the dependence: the Kept log card, when it has no
key, offers a box to type it into, remembers it on that browser exactly as
the address bar would have, and opens both keyed cards at once. The box is
a password field, the entry is trimmed, an empty entry does nothing, and
the browser test types the key in after forgetting it and finds both cards
open on the next plain visit. The keyed URL still works where it arrives
whole; the operator no longer needs it to.

## Addendum, 2026-09-13: the rows are off

Steve, late that day: "I guess disable the per visitor data for
now." Done as one setting rather than a removal, because the day's evidence
is that this decision moves: `Admin:VisitorRows`, off by default. Off, the
visitor table and the kept log answer 404 to everybody, with the key as
without it, the public report says `visitor_rows: false`, and the page
shows neither the table, the kept log card nor anything about them. The
rows keep being written: the counters the graph is drawn from are the same
documents, the kept log is the record he asked for an hour earlier, and
turning the setting on shows what was kept meanwhile. The graph of unique
visitors per day stays public; it names nobody. The operator's key stays
for the one card that still needs it, the reset links, and its box moved to
an Operator card of its own. A test holds the 404 with the key on a host
with the rows at their default.


## Addendum, 2026-09-14: one keeper, and it is the document store

The rule above, each store keeps the rows for the requests it served and the
report reads both, met the free tier on the fourteenth. Azure SQL Database's
free amount is 100,000 vCore-seconds a month, and a serverless database
pauses only after an idle hour; a collector writing a batch every five
seconds is never idle. The database that had paused between visits since
the third of September stopped pausing on the thirteenth, when this record
shipped, and at 06:40 UTC on the fourteenth Azure paused it for the rest of
the month with its own sentence in the exception (error 42119): the free
amount will renew on the first of October. The site kept serving, from its
files, with no accounts and no bids, and the activity card on both sites
answered 500, because the SQL half of the read threw before the Cosmos DB
half was asked. Measured off `az sql db show` (status Paused, pausedDate
2026-09-14T06:40:21Z) and off the container's log, not inferred.

Two changes, and Steve named the first: the activity should live on Cosmos
DB, permanently. The collector now writes every batch to one keeper, Azure
Cosmos DB wherever it is configured, whichever store served the request;
the row still carries the serving store's key, so the graph keeps its line
per store and the comparison stands. The report reads the keeper once and
splits the rows by that key, and a store that is down cannot take the card
with it. Without Cosmos DB on a container the default store keeps its own
rows, which is what the test host does. The second is in the store's own
record: the site now points at a Basic database beside the paused one
(ADR: The SQL Server backend, addendum of 14 September).

What it costs: the activity rows were already on Cosmos DB with no expiry
(the addendum of the thirteenth); this moves the SQL site's share there
too, a few hundred request units a day inside the free tier's thousand a
second. What it stops costing is the whole of the free relational amount.
A test holds the keeper: two stores, two hits naming each, both rows land
on the keeper and none on the other.

## Addendum, 2026-09-24: people, scanners, and the site reading itself

The card said 710 people in a day. Read off the kept rows for the eight days
to 24 September before anything changed (activitylane, queue 1347): from 20
September, the day both sites moved to App Service, 375 to 643 of each day's
"people" were tokens with one request each, all of them `/index.html`, all
from the loopback address with a port after it (`127.0.0.1:8069`, one port
per request), 643 of 757 on the twenty-third. Requests from the machine
itself: the port went into the token, so every one was a new visitor, and
into the network, which showed the whole address. What sent them is not
attributed here, because a row keeps no user agent; the shape (loopback, one
request, the landing page, a few hundred a day on each site) is what was
measured. The session key
compounded it until 24 September: with the placeholder key each process
invented its own, so one address was a new token after every roll and on
each site.

Three kinds now, each visitor-day exactly one of them, adding up to the
day's total, and a toggle on the card, Visitors only by default:

- **The site's own reads.** A row from the loopback address, which is this
  machine by definition, read from the network the row already keeps, so the
  rows written before this addendum read the same way as the rows after; all
  of a day's loopback rows are one visitor-day, the machine. And a row kept
  under the self mark: the tools that read the site on its operator's behalf
  (the page sweep, the ship's readers, the card's own pictures) carry
  `TheYard-SelfRead` on their user agent, as do App Service's own agents
  (AlwaysOn, the health check), and such a request is kept under a token of
  its own (the keyed hash of the mark and the address) and the network
  `self:x`, so it never folds into the row of a person at the same address
  and names no network. A stranger can claim the mark and hide from the
  card; that costs a count of visitors and reaches nothing else.
- **Scanners and crawlers.** A token whose every request looked like a bot,
  by its agent or by what it asked for, the rule this record started with.
- **People.** Everybody else: a token any store saw make a request that did
  not look like a bot.

A port after an address is dropped before the token and the network are
made, so one machine is one visitor whatever port it came from. What a hit
carries is unchanged: no user agent is kept, the agent is read on the
request and forgotten, as it always was for the bot guess. The kinds and
what each asked for are counted from the visitor rows the days already
came from, one read, and returned beside the days (`who` in the report);
the rows still leave the server only through the keyed endpoint. Tests hold
the mark, the port, the loopback networks, the one-machine rule and the
three kinds adding up.

The chart, from 1.0.3.12, stacks the three kinds day by day rather than
drawing one line per store: people at the bottom, then scanners and
crawlers, then the site's own reads, in three tokens chosen as a set and
checked for colour vision together (the Colour and style page, its chart
colours), each band's name written on the band where it is as thick as a
line of text, a legend for all three, and the day-by-day numbers in a
table under the chart. The lines by store are the other view, a click
away; the split by store stays in the line under the chart in both.

The path to the resume, from 1.0.3.13: four steps, counted in visitor-days
from the same rows, each a set of paths (the page, which every address the
site serves is kept as; `/api/vehicles`, the inventory's listing;
`/api/docs/author`; and the resume, `/api/docs/resume` as the page links
it and `/docs/resume.pdf` as the repository serves it). Read off the rows
before it shipped: of the 41 resume requests in the eight days to 24
September, 39 came through `/api/docs/resume`, which is why the step
names it first.

From 1.0.3.16 the card names what people asked for by page, from a table
of patterns in `src/lib/activity.ts` (the most particular first, a path
nothing names shown as itself), shows what scanners probed apart from it,
and puts the collector's counts behind a Details line that says whether it
is fine.

## Addendum, 2026-09-24: where they came from

From 1.0.3.17 a hit carries a seventh field, on a page load only: the host of
the page that linked here, lowercased, and nothing else of the referrer, so
a path or a query that could carry something about the visitor never
reaches a row. A referrer that is this site, the other store's site or the
App Service origin is moving within the site and is no source; no referrer
is kept as "(none)". The hosts are counted on the visitor row the way the
paths are, top twenty, in both stores: a field on the Cosmos DB document,
and a nullable `Sources` column on `ActivityVisitors` in the database
project and in a SQLite migration. The relational column reaches Azure SQL
Database at the next deliberate publish (ADR: Data first, and the database in source
control). Until then the relational activity store finds the column
missing and refuses itself, naming it, the way a missing table refuses it,
rather than failing every batch; that matters only on a start where Cosmos
DB did not answer, because Cosmos DB keeps the activity on both sites.

The hosts are public on the card, beside the five groups the card draws
(LinkedIn, GitHub, search, another site, typed or unknown). Three ways were
put to Steve with a picture of each: the group alone, the plain host, and a
keyed hash of the host. He chose the plain host, and on keeping it behind
the operator's key: "no reason that should be behind a operator key no
personal information", and "we would only obscure personal information
and that is not personal". A referring site names a site and not a person. A
link opened from a PDF, the resume among them, sends no referrer, so it
reads as typed or unknown unless the link itself carries a tag.

## Addendum, 2026-09-25: what the card said it did, and what it did

A read of this record against the code on 1.0.3.22 found the card promising
three things it never showed and saying two things that had stopped being
true. From 1.0.3.23:

- **A dropped hit is counted.** The channel drops the oldest hit when it is
  full, and with that mode the write that causes the drop still succeeds, so
  the collector counted every offered hit as offered and nothing counted the
  drop, though this record and the collector's own summary said it did. The
  channel's callback for an evicted item now counts it; the report carries
  `collector.dropped` and the Details line says it. A test fills the channel
  past its ten thousand and reads the count.
- **The request-unit cost is on the card.** The document adapter counted
  what the feature cost it from the start, as "What the feature costs the
  stores" above says the card shows, and nothing read it. The report carries
  `cost` (request units, operations, failures since the process started)
  when the keeper has a unit to count in, and null on the relational store,
  which has none; the Details line says it.
- **The retention is on the card while the store is up.** The keeper's own
  sentence ("kept in Azure Cosmos DB with no expiry") reached the page only
  as the reason a store was unavailable. The report carries it as
  `retention` and the card shows it under the totals.
- **One keeper, said so.** The Details line said "one batch per store"
  after the 14 September addendum made every batch go to one keeper; it
  names the keeper now, and so does the collector's summary.
- **The About text says what the card shows.** It led with the per-store
  lines the card drew before 1.0.3.12 and described a visitor table behind a
  key, where the rows have been off since 13 September. It opens now with
  visitor-days stacked by kind and says no list of visitors is shown.

On the 24 September addendum's sentence "What sent them is not attributed
here": it was true of the rows, which keep no user agent, and the sender was
named the same day off the configuration and the kept log (activitylane,
queue 1353): both App Services run `alwaysOn` with `healthCheckPath` set to
`/healthz`, and the kept log held 204 loopback lines per store that day,
every one `GET /index.html`, a median of 300 seconds apart. The card has
named App Service as the sender since 1.0.3.11; the sentence above stays as
it was written.

## Addendum, 2026-09-25 (1.0.3.26): the retention and the cost, said precisely

The card's retention line read the store's availability sentence, the reason a store gives when it is down, so on SQLite it said "kept in SQLite", a place and not a time. The port carries the retention as its own field from 1.0.3.26, and every store states it: Azure Cosmos DB from the container's time-to-live, the relational stores "with no expiry", which is what they do. The cost is a record, `ActivityCost`, counted with interlocked adds (the charge in hundredths of a request unit), because the batch writer and every report request count at once and a plain `+=` lost updates. The card says whose it is: this container's, since it started; the other container writes to the same keeper and counts its own. The About text no longer says no list of visitors is shown: the per-visitor rows, hashes only, are served to the operator's key on a site that turns them on.

## Addendum, 2026-09-28 (1.0.3.35): the first read, and where its two seconds went

Measured on the Cosmos DB site from Missouri that morning, the first `GET /api/admin/activity` after a quiet spell took 2.0 to 2.2 s to its first byte, and the next about 260 ms. The workbench reads the week on mount and the card opens on the week, so that first read was the week's.

Where it goes, timed step by step with a stopwatch against the live activity container from Steve's machine (a one-off test, read only, not kept; the figures include the trip from Missouri to the region, so the large reads are slower here than inside Azure, but the shape is the point):

| Step | First use | Warm, two more passes |
| --- | --- | --- |
| The credential's first token | 2,964 ms | held |
| The container's first read (the availability check) | 2,985 ms | 64 to 65 ms, then held |
| The day's hour rows, 50 of them | 1,432 ms | 59 to 61 ms |
| The day's visitor rows, 170 | 488 ms | 134 to 192 ms |
| The week's visitor rows, 3,661 | 3,439 ms | 2,754 to 3,137 ms |
| The month's visitor rows, 5,279 | 8,017 ms | 4,064 to 5,080 ms |
| Folding the rows into the report | 46 to 73 ms | 1 to 101 ms |

One read of each window cost 312 request units, nearly all of it the visitor rows. Two costs, then. A process's first use of the store pays for a token and for waking its connections, which nothing in the report can remove. And every report, first or not, reads every visitor row of its window to count the people, the scanners and the site's own reads: the largest cost of any read of the week or the month, and the one a reader was waiting on.

**The fix: the report is kept, and rebuilt behind the reader** (`ActivityReportCache` in `api/TheYard.Api/Activity.cs`). A window's report younger than thirty seconds is served as it is. One older than that, up to ten minutes, is served at once while a new one is built behind it, so the next reader gets the new one. Only with no report, or one older than ten minutes, does a reader wait for the build, and however many ask at once, one build runs per window. The numbers on the card are at most thirty seconds behind the rows when anybody is reading it, and a reader after a quiet spell sees the last report while the new one is counted. The warm read is the time to serialise a report already built, under the 260 ms it was. Once the site is kept awake, a report is never ten minutes old, so no visitor waits on a build. `ActivityReportCacheTests` holds the thirty seconds, the ten minutes, the one build and a failed rebuild leaving the old report in place.

**`/api/health`'s first read** (736 ms against about 190 warm) runs one probe per store (Azure SQL Database, and two point reads of Azure Cosmos DB) beside three file checks, each timed, and nothing it does is heavy. The difference is the connections: after a quiet spell both stores' connections have to be opened again, and the edge's to the origin with them. That is not a cost in this code, and the keep-warm loop is what answers it.

## Addendum, 2026-09-29 (1.0.3.48): a Site traffic section

Steve: "should we add a section to the documentation on site traffic like we did for styling?", and then "Go". The counting, the keeping and the finding were told only in two records and on the Admin tab's cards, and nothing tied them together for a reader who is not reading records. The Site traffic section, under How it is run beside Performance, is four pages on the Style section's pattern: a landing page with three tiles, the standing rules, a glossary and readouts counted from the build, then Who comes (what a visit carries, the three kinds, where readers came from, the path through), What is kept (the counters, the kept log, how long, public and keyed, what is never kept) and Being found (robots, the sitemap, /about, the structured data, llms.txt, Search Console). Every sentence on them is drawn from this record, ADR-073, ADR-053 and the code, and the pages show the code itself where it decides (the hit's fields and the visitor token, as live blocks). Nothing about how a visit is counted changed.
