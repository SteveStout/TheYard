# What is watched

Every monitor that watches TheYard, in one table: what it reads, how often, where the reading is kept, and what red means. Each is on the [Admin tab](https://theyard.stevenstout.biz/?view=admin) or one click from it. A monitor that exists in code and is missing from this page is a gap; the reverse is a promise this page cannot keep.

## In plain words

A site that nobody watches breaks quietly. This page lists every check that watches this one: the ones Azure runs from outside, the ones the app runs on itself every few seconds or minutes, and the ones that run when a new version goes live. For each, it says what a bad reading looks like, so a reader knows what would wake somebody up.

## The table

| Monitor | What it reads | How often | Where the reading lives | What red means |
| --- | --- | --- | --- | --- |
| App Service health check | `GET /healthz` on the one web app and on the rendering service | about once a minute, by Azure | Azure, the web app's health | the instance answered with an error for long enough that Azure replaces it |
| Liveness, `/healthz` | the process answers at all | on request; Azure's check above | not kept | the process is down |
| Readiness, `/readyz` | the files are in place and the relational store's catalogue has loaded | on request; every deploy waits on it | not kept | a new container is not ready for visitors yet |
| Store health, `/api/health` | each store this process runs: reachable, catalogue loaded, which provider | on request, and by the Admin tab's Health card | not kept | a store is down and the site serves from the file fallback, which the deploy also warns about |
| The Shed's health | The Shed's home folder is there, on its own name | Deploy reads it at every roll; App Service's own check reads TheYard's `/healthz`, not The Shed's, so between rolls nothing watches The Shed but a visitor | not kept | The Shed's application is not answering on its name, or did not start, which its name answers with a 503 and TheYard's log says why |
| Machines, `/api/admin/machines` | the process's working set, managed heap, processor, threads, collections, and catalogues held in memory | every 15 seconds in memory; each finished minute kept | Cosmos DB, 31 days | memory climbing, or a catalogue held in memory again |
| Memory against traffic | working set and processor plotted against requests a minute | every load of the card, over the kept minutes | read from the machines history | memory climbing while traffic is flat: a leak or an unbounded cache (ADR: Memory follows traffic) |
| The App Service plan card | the plan's memory and processor, each app's memory and processor, the platform's share outside the apps (derived) | every minute from Azure Monitor; hourly roll-ups | Cosmos DB, minutes and hours | the plan above its amber or red line, an app stopped, or a restart Azure did not plan (ADR: The App Service plan, read every minute) |
| Azure events row | Azure's own restarts and platform events on the plan | hourly with the plan's hours | Cosmos DB | an unplanned restart in the window |
| Resource cards | Azure SQL Database, Cosmos DB, the registry, Application Insights, the mail service and the Netlify edge, each read by its own metrics | every minute where Azure has a metric, hourly otherwise | Cosmos DB, per resource | the resource past its own threshold, said on its card (ADR: One frame for every resource) |
| Cost cards | what Azure bills each resource, by day | hourly | Cosmos DB | spend outside what the records priced |
| Application Insights | requests, dependencies and exceptions from the process | continuously | Application Insights, Azure's retention | exceptions or failed dependencies; read behind a sign-in |
| The kept log | every log line TheYard writes, and The Shed's failures past its own handler | drained every 60 seconds | Cosmos DB | errors in the log, readable a day, a week or a month back; The Shed's own log lines go to the container's console |
| Error record, `/api/errors` | every 500 and unhandled exception, type and frames | on each one | in memory, and the kept log | a server error a visitor received |
| Site activity | requests, visitors and the site's own reads, counted apart | collected every 5 seconds | Cosmos DB | traffic that is not what the window should show |
| The proof card | a visitor's requests sent to both stores in this process | when the operator runs it | the card | the two stores answer the same request in measurably different times, with the reason |
| Keep-warm | every public read through each store | every 4 minutes | counted on the Admin tab | a read failed between visitors |
| Hourly readings | the certificate the site answers with, and how full the relational store is | hourly | in memory, on the Azure card | a certificate near expiry, or a database near its size |
| The page sweep | every address the process serves | at every roll | `/api/admin/pages` | a page that answered anything but what it should after a deploy |
| Deploy's Verify | the version at the origin, readiness, the Cosmos DB site's name, The Shed's name, then both Yard domains | every push to main | the workflow run on GitHub | the roll is red and the edge does not move |
| Edge branch | both Yard names serve the build after the edge moves | every push that changes the edge | the workflow run on GitHub | the edge rules moved and a name did not follow |
| CI | every suite again on a clean runner, with coverage | every push to main | the workflow run on GitHub, the README badge | a check only CI measures went red on that commit |

## Where it sits

Nothing here is new code: this page is the index of monitors that already exist, so the next architect sees the whole watch at once. The readings that outlive the process are kept in Cosmos DB beside the documents; the ones that matter only now stay in memory. Each row names the record that explains its threshold.

## Files

- [`api/TheYard.Api/Features/Health/HealthEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Health/HealthEndpoints.cs) - `/healthz`, `/readyz` and `/api/health`
- [`api/TheYard.Api/Machines.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Machines.cs) - the sampler, every 15 seconds
- [`api/TheYard.Api/PlanRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/PlanRecorder.cs) and [`api/TheYard.Api/CloudRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CloudRecorder.cs) - the plan and the resources, every minute
- [`api/TheYard.Api/CostRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/CostRecorder.cs) - the bill, hourly
- [`api/TheYard.Api/KeepWarm.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/KeepWarm.cs) - every public read, every four minutes
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/edge-branch.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/edge-branch.yml) - what every roll reads
