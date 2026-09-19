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

## Addendum, 2026-09-19, shipped as 1.0.0.150: the same readings, drawn

Steve, on the card as it first shipped: graphs of the processor and memory of every Azure resource
this site runs, one chart per resource, the way the site activity is drawn. Three tables of numbers
are a reading; a line is a shape, and the shape is what says whether a container is climbing.

One chart per resource, and the unit is the resource's own. The container's two lines are
percentages: memory as a share of the limit the container group granted, and the processor share
already measured that way, so both sit on one axis. The relational store's two are percentages
because that is what its own view reports, of what the tier allows. The document store has no
processor or memory to draw, so its chart is what it does have, request units a minute, on an axis
of its own, and the sentence beside it still says why the other two lines are missing.

A percentage chart keeps a full axis whatever the hour held: a quiet hour drawn against its own
maximum looks like a busy one, which is the chart lying with true numbers. The arithmetic lives in
`src/lib/machineChart.ts` with the activity chart's, and a missing reading breaks the line rather
than being drawn across, because the first processor share of a process has nothing to compare
against and a line through it would be a number nobody measured.

### What the first live read found

Two things, and both are in this version rather than in a note. The limit the container's memory is
read against is the runtime's own, `TotalAvailableMemoryBytes`, which on this container group is
1,057 MB against the 1,536 MB the group granted: .NET works to about seven tenths of a container's
memory by default, and a process that passes its own limit is the one that gets collected. The card
says which limit it is showing rather than letting a reader assume the other one.

And the relational store's resource view answered with a `SqlException` on both sites, so the card
showed its absent reading exactly as designed. Reading the view needs `VIEW DATABASE STATE`, which
this container's identity may not hold; the reason now goes to the container's log, where an
operator can read it, while the public card still says only the type. A reading that fails politely
and says where the reason is beats a card that shows a zero.

That last sentence was half wrong when it was written, and 1.0.0.151 is the correction: the log ring
this site serves keeps an exception's **type** and not its message, so "the container's log carries
the reason" was true of the container's stdout and not of anything a reader could open. The card now
carries the database's own error number instead, and for the two numbers this view answers
permission with, and 262, the one both live sites answered with when this shipped, a sentence with
the answer in it: the view needs `VIEW DATABASE STATE`,
and `db_datareader` and `db_datawriter`, the two roles this container's identity holds
(ADR: The SQL Server backend), do not carry it. A number names no server, which is why it can be on
a public page when a message cannot.

### The cast, and the four minutes it took

`sys.dm_db_resource_stats` returns its percentages as `decimal(5,2)`. The record read them into
doubles, which is an `InvalidCastException`, and the endpoint answered 500 on both live sites from
the minute the `VIEW DATABASE STATE` grant let the query run at all until the fix rolled. It could
not have failed earlier: before the grant the query never got as far as a row, so the shape of a
row was never tested against the real view. That is the class of defect a suite on SQLite cannot
catch and a live read can, and the live read is what caught it, in Steve's own Admin tab.

Two things changed. The statement casts each percentage to float, in the statement rather than in
the type, because these are percentages a chart draws and not money. And the page sweep asks for
the Admin tab's own readings now, so an endpoint that throws shows up as a page that is down at the
next roll rather than in a card somebody happens to be looking at.

## What it costs

Nothing on the bill. The sampler is a timer in a process that is already running; the resource view
is one SELECT against a database already connected, excluded from the SQL card for the same reason
the health check is, so the card cannot fill the page with the act of reading it; the document
reading is arithmetic over a ring the container already keeps. No tier, no resource, no metrics API.

## Files

- [`api/TheYard.Api/Machines.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Machines.cs): the sampler, the resource view, and the document reading.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs): the sampler registered, the endpoint, and the statement kept off the SQL card.
- [`src/components/AdminPanel.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/AdminPanel.tsx): the card, three readings with their three honesties, and the chart each one is drawn in.
- [`src/lib/machineChart.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/machineChart.ts): the chart arithmetic, React-free and tested on its own.
- [`api/TheYard.Tests/MachinesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/MachinesTests.cs): the ring, the first sample, the folding, and the shape the endpoint answers with.
- [`docs/ADR-077-every-page-checked.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-077-every-page-checked.md): the other card this morning added, and the sweep whose requests this one's numbers include.
- [`docs/PERFORMANCE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/PERFORMANCE.md): the claim about one small container that this card is the running proof of.
