# What is kept

**Two things outlive a restart: the activity counters the graph is drawn from, and the kept log.** Both live in Azure Cosmos DB and both are written in the background. Neither holds anything that names a person.

## In plain words

This page says what a visit leaves behind after the site restarts: running counts of visits (the activity counters) and a log of requests and errors (the kept log). Both are kept in a database (Azure Cosmos DB) and neither holds anything that names a person. It also says how long each one is kept.

What that is worth: a developer can see how to keep a traffic history that outlives a deploy without storing personal data, and the organization keeps its records for years with no clean-up job to run.

## The activity counters

One document per store per hour, and one per visitor per store per day, each holding counts plus the top twenty paths. Both sites write to one keeper, Azure Cosmos DB, whichever store served the request. Each row carries the serving store's key so the graph keeps its line per site. The counters add rather than overwrite: two containers write the same documents, and the database applies each increment itself.

**Nothing waits on the store.** A request hands its visit to a bounded queue and leaves. A background service drains the queue every five seconds, or sooner when five hundred are waiting. It folds the batch into one change per store and hour and one per store, day and visitor, and writes it. A full queue drops the oldest visit rather than slow a request down, and the card says so.

## The kept log

One document per event in a container named `logs`, partitioned by the day: every request the activity counter counts, every unhandled error (the server's and the browser's reports), and every warning the application writes. A background writer drains its own queue once a minute into one batch per day.

Beside those, every entry the Admin tab's public lists take, recent errors, the log as the console got it, the SQL the application ran and what the document store ran, is kept whole as the entry the list serves. Those lists are rings in memory and empty on every deploy; kept, a card can show the last day, week or month.

Every string on its way in passes through one function: bounded in length, and with every at sign turned into `%40`, so an address in a path, a query, an exception message or a stack cannot be kept as one. The function, read from this build:

```live path=api/TheYard.Application/Logs.cs region=log-text
```

## How long

- The activity counters: kept for good. The documents are small, a few hundred a day, a few tens of megabytes a year.
- The kept log: three years, set as the container's time to live, so the database removes an old document and nothing runs to delete it.
- The public lists, kept: thirty-five days each, by the document, because the widest window a card offers is thirty and a document has to outlive the window that reads it.

## Public and keyed

- **Public:** the Admin tab's activity card and its report, `GET /api/admin/activity`, the totals by day, kind, store, page and referring site. It names nobody, and a test holds it to that. The kept public lists, `GET /api/admin/kept`, are public for the same reason the lists always were.
- **Keyed:** the per-visitor rows, `GET /api/admin/activity/visitors`, and the kept log, `GET /api/admin/logs/kept`. Both answer only to the operator's key, compared in constant time, and a wrong, missing or unset key is a 404, so a stranger cannot tell either exists. Even behind the key, a row is a token plus three octets.

## What is never kept

No email address, no account, no full IP address, no user agent, no query string, no referring page beyond its host. The types the visits are made of have no field for any of them, and the tests check the consequence on the wire: after requests that put an address in the path and in the query string, no response contains an at sign.

## The decisions behind it

- [ADR-071, Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity)
- [ADR-073, Logs that outlive the container](https://theyard.stevenstout.biz/?doc=adr-kept-logs)
