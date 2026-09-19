# ADR: What the machines are doing

Status: accepted, 2026-09-19, shipped as 1.0.0.149. Asked for in one sentence: the SQL and memory
performance of the relational store, the document store and the container, on the Admin tab, kept
the way the site activity is kept.

## Context

The Admin tab could already say what the application did: every SQL statement with its milliseconds,
every document-store operation with its request charge, the timing of every path, and eight paired
rounds proving the two stores answer the same. What it could not say is what any of it cost the
machines underneath. A container with 1.5 GB and one processor serving a hundred thousand vehicles
out of its own memory is the claim the Performance overview makes, and nothing on the site showed
the memory it was talking about.

The reason is in the shape of every other reading here: they are all taken when something happens.
A request is timed because a request arrived. A statement is recorded because a statement ran.
Memory is not an event. Nothing was going to show it until something asked on a clock.

The three machines also owe three different answers, and pretending otherwise would be the easy
mistake here:

- **The container** knows its own memory and its own processor time exactly, from the runtime.
- **Azure SQL Database** keeps its own reading of itself, `sys.dm_db_resource_stats`, fifteen seconds
  at a time for the last hour, on every tier including the Basic one this site pays $4.90 a month
  for. It costs one SELECT to read and nothing to keep.
- **Azure Cosmos DB** has no memory reading to give, and no processor reading either. It is sold by
  request unit, and what it will tell you is what each operation charged.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| **Sample the container on a timer, read each store the way that store reports itself** | Every number comes from the thing it is about, at no charge, and the card can say plainly where each one came from | Three sources and three shapes on one card, and a timer that has to be kept cheap |
| Azure Monitor metrics for all three | One shape for everything, and history that outlives the container | A metrics read is an Azure API call with its own permission and its own bill, and the container's managed identity would need a role it does not have today |
| Application Insights | Already configured, already collecting | It samples what the application does, not what the machine has; the memory counters it does carry are the ones this sampler would be reading anyway, an hour late |
| Leave it to the Performance overview | Nothing to build | The page quotes numbers a reader cannot check, which is the one thing this project does not do |

**Decision: sample the container, and ask each store for the reading it actually keeps.**

## The three readings, and what each one is worth

**The container.** A hosted service takes a sample every fifteen seconds and keeps the last two
hundred and forty, which is an hour in about twenty kilobytes. Each sample carries the working set
as the operating system sees it, the managed heap as the collector sees it, the share of the
container's processors this process used since the previous sample, the thread count and the
collection counts. The first sample reports no processor share: there is nothing to subtract from,
and a percentage averaged over the whole life of the process wearing an interval's label is a wrong
number rather than a missing one. The limit the percentages are read against is the runtime's own
`TotalAvailableMemoryBytes`, which is what the container group's definition granted.

**The relational store.** One SELECT against `sys.dm_db_resource_stats`, which returns the last hour
as processor, data, log, memory and worker percentages of what the tier allows. On the Basic tier
those are percentages of five DTUs, which is the honest way to read them: a number here is a share
of a very small machine. The view exists on Azure SQL Database and nowhere else, so on SQLite, on a
containerized SQL Server, and on a database that did not come up, the card says which of those it is
looking at instead of drawing a zero.

**The document store.** No memory, no processor, by the nature of the product: this card says so in
those words rather than leaving a gap a reader has to explain to themselves. What it shows instead is
the operations ring folded into request units a minute, beside the thousand request units a second
the free tier allows, so the one number that could ever cost money is the one on the card.

## What it costs

Nothing on the bill. The sampler is a timer in a process that is already running; the resource view
is one SELECT against a database already connected, excluded from the SQL card for the same reason
the health check is, so the card cannot fill the page with the act of reading it; the document
reading is arithmetic over a ring the container already keeps. No tier, no resource, no metrics API.

## Files

- [`api/TheYard.Api/Machines.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Machines.cs): the sampler, the resource view, and the document reading.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the sampler registered, the endpoint, and the statement kept off the SQL card.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card, three readings with their three honesties.
- [`api/TheYard.Tests/MachinesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/MachinesTests.cs): the ring, the first sample, the folding, and the shape the endpoint answers with.
- [`docs/ADR-077-every-page-checked.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-077-every-page-checked.md): the other card this morning added, and the sweep whose requests this one's numbers include.
- [`docs/PERFORMANCE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md): the claim about one small container that this card is the running proof of.
