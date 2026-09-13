# ADR: Logs that outlive the container

Status: accepted, 2026-09-13, shipped as 1.0.0.118. Steve's ask, the same
afternoon the visitor table went behind a key: "I want to make sure we have
long logs, any way to capture those in SQL?", then "do whatever you think is
best for the logs, SQL or Cosmos DB, is Cosmos better since it's
unstructured?"

## Context

Three things this site calls a log, and how long each lasted before today.
The Admin tab's rings (the last five hundred requests, the last three hundred
log lines, the last fifty errors) are this process's memory and empty on
every roll, which the page says out loud (ADR: Observability). Application
Insights keeps every request and exception for thirty days on the free tier,
behind a sign-in, and does not know which store served a request (ADR:
Telemetry). The activity counters from this
morning last thirty-five days on the document store and indefinitely on the
relational one, but they are counters: they can say how many, never which
(ADR: Site activity, and the line an address does not cross).

So the honest answer to "do we have long logs" was no. After a roll the
operator could not say what the site had served an hour earlier, and after a
month nobody could say anything.

## Decision

**One document per event, in the document store, for a year.** A container
named `logs`, partitioned on the UTC day, holding three kinds of event under
one spine (when, kind, store, level, method, path, status, duration, visitor
token, network, message, detail, trace id): a `request`, an `error` (an
unhandled exception or a browser report, with its type, message and a
bounded stack in the detail) and an `app` line (a warning this application
wrote). The container's time-to-live is the retention policy: a document
expires a year after it is written and nothing runs to delete it.

**Why the document store, and what "unstructured" actually buys.** The word
is close and the reason is more specific. A log is append-only, written all
day and read now and then, and its three kinds of line have three shapes. A
document takes each shape as it arrives, so a fourth kind next year is a new
value in one field, with no migration behind it. A write is a create and never a
merge, so a hundred of them go in one transactional batch for a few request
units. Retention is a number on the container. And a writer that runs once a
minute never keeps a database awake, which matters on the relational side:
Azure SQL Database here is the serverless free offer, metered in vCore
seconds, and a log that wrote every minute would spend that allowance keeping
the database from pausing. The relational store keeps the counters the graph
is drawn from, which is the query that wants a table and an index; this is
the other kind of data, and it goes to the other store. Both containers write
to the same container with a `store` field saying which engine served the
request, so the split the activity graph shows is in the log too.

**Nothing waits on the store.** The same shape as the activity collector and
for the same reasons: a request offers its event to a bounded channel and
leaves; a hosted service drains the channel once a minute, or sooner when
five hundred are waiting, and writes one batch per day partition. A full
channel drops the oldest event rather than blocking a request, and the card
says how many were written and how many batches failed. A failed batch is
counted and never logged, because a log line about the log failing would
arrive back at the same collector.

**Three sources, one rule.** The request hook beside the request ring offers
one event per request, with the same exclusions as the activity feature (the
page's own files, the photos, this tab's reads). A logging provider offers
this application's warnings and errors, on the same allow-list of categories
as the Admin tab's ring plus the one framework category that reports an
unhandled exception; Information lines are the request log's job and are not
kept twice. The browser's error reports are logged on arrival and so reach
the provider like any other error. Every string field passes through one
function on the way in: bounded, and with every at sign turned into `%40`, so
an address in a path, a query string, an exception message or a stack cannot
be kept as one. The endpoint cleans once more on the way out, and a test
holds the absence of the at sign on the wire after an address was written
into a path and posted as a browser error.

**What is kept about a person, and what is not.** The visitor token and the
network are the activity feature's, and nothing more: a daily keyed hash and
three octets. The exception message is kept here where the public ring keeps
only the type, and the difference is the key: the ring is on a public page
and this endpoint answers only to the operator (ADR: The code is public and
the secrets are not). One thing changed on the public side while this was
built: a browser error report's message and stack are now cleaned on arrival
too, because the same text was going to the public errors ring unchanged.

**Read back from the site, behind the key.** `GET /api/admin/logs/kept`
answers to the operator's key like the visitor rows do, and is a 404 without
it. It takes a window (24h, 7d, 30d) and optionally a kind, a status and a
fragment of the path, which travel to the store as query parameters and never
as syntax, and returns the newest two hundred with the window's counts by
kind and what the feature has cost. The Admin tab's Kept log card sits under
the activity card: a window, three filters, and the lines grouped by day.

## Alternatives

**Tables in Azure SQL Database.** The first reading of the ask, and rejected
for the reasons above: the free offer's meter, a purge job instead of a
time-to-live, and three shapes in one table or three tables for one log.
Nothing here needs a join or a cross-cutting query that the counters do not
already answer. If a query ever does, the document store's own SQL answers
it, and a table is one adapter away behind the same port.

**Application Insights, longer.** Retention can be raised to two years for a
charge per gigabyte per month, and the query language is better than any
table this project would write. Rejected for the same reasons the activity
feature gave: the store split is not in the telemetry, nothing about it runs
in the gate, and the operator reads it in the Azure portal rather than on the
site, which is the surface this project is about.

**The rings, made bigger.** A larger ring is still this process's memory and
still empties on a roll. Rejected without much thought.

**Keep everything, forever.** At today's rate a year is a few hundred
thousand documents and well under a gigabyte, so forever would be affordable.
A year is chosen because an old log is a liability with no reader: nobody
diagnoses a fault from fourteen months ago, and a network beside a timestamp
is still a network beside a timestamp. Reversible in one number on the
container.

## Consequences

- A third home for a log, and the first that is both durable and readable
  from the site: the rings for the last few minutes, Application Insights for
  thirty days with a query language, the kept log for a year with the store
  split and the operator's key.
- The `logs` and `tests-logs` containers exist on the account; the definition
  is `infra/cosmos/logs.json` and a test holds the code to it.
- A container with no document store configured (a developer's machine, the
  SQLite gate pass) keeps nothing and its card says so; the endpoint's shape
  is the same either way.
- The drain interval is configuration (`Logs:DrainSeconds`, a minute by
  default) so the browser suite can run it at two seconds and wait for a line
  instead of a clock.

## Files

- [`api/TheYard.Application/Logs.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Logs.cs): the event, the cleaning rule, the query and the port.
- [`api/TheYard.Api/Logs.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Logs.cs): how a request or a log line becomes an event, the collector, the logging provider and the report.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the wiring beside the rings, the hook beside the request ring, and the keyed endpoint.
- [`api/TheYard.Infrastructure.Cosmos/CosmosLogStore.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosLogStore.cs) and [`infra/cosmos/logs.json`](https://github.com/SteveStout/TheYard/blob/main/infra/cosmos/logs.json): the adapter and the container it needs.
- [`src/lib/logs.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/logs.ts) and [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card.
- [`api/TheYard.Tests/LogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LogTests.cs) and [`tests/e2e/admin.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/admin.spec.ts): the cleaning, the collector, the provider, the endpoint, and the at sign that is never there.

```live path=api/TheYard.Api/Logs.cs region=collector
```

```live path=api/TheYard.Api/Program.cs region=kept-logs-endpoints
```

## Addendum, 2026-09-13: three years

Steve, an hour after this shipped: "But I want long term logs ... CosmosDB
for logs." The year above was chosen for the reason given, an old log with no
reader; his reading is that the log is the record and the record is worth
keeping, and it is his site. The `logs` container's default time-to-live is
now three years (94,608,000 seconds), applied to the live account and read
back, and the card says the number it reads. At today's rate that is under
a million documents and a few hundred megabytes on the free tier's
twenty-five gigabytes. The activity counters went to no expiry at the same
time (ADR: Site activity, and the line an address does not cross, third
addendum). Reversible in one number on each definition.

