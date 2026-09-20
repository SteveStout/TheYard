# ADR: One plan, two sites

Status: accepted, 2026-09-20, written before any of the resources it names existed. Asked for in four
sentences, reading the machines card the evening it went live: "We're using about 1% of the SQL, is
there any way to reduce our SQL cost?", then "Can't we use one container?", then "Is there any way
for them to share an App Service plan instead, which would be cleaner?", then "or share compute."

## Context

The machines card (ADR: What the machines are doing) put three readings on one page for the first
time, and the first thing they said was that the bill was in the wrong place. The relational store
was at about one per cent of its five DTUs. It is also already at the floor of its pricing: Azure
SQL Database Basic is $0.161 a day, $4.90 a month, and utilisation buys no discount. There is no
smaller tier to move to.

The money was the compute. Two container groups, one per site, each 1 vCPU and 1.5 GB in West US 2
at $0.0405 a vCPU-hour and $0.00445 a GB-hour, which is $34.44 a month each and $68.88 for the pair.
Both run the same image (ADR: One container, both stores) and differ in three environment
variables. Their own samplers read 0 to 20 per cent of one processor and a working set of about
580 MB apiece, against a runtime limit of 1,057.5 MB. Two machines were being paid for twice to run
one image at a fifth of one processor.

Container Instances bills for what a group is granted, by the second, whether or not anything uses
it. An App Service plan bills for a machine, and every web app on the plan shares it. That is the
whole idea: one machine, two sites.

## What was considered

Every price is Azure's own list price, read off the Retail Prices API on 19 and 20 September 2026,
over 730 hours.

| Option | Compute a month | What it buys | What it costs |
| --- | --- | --- | --- |
| Two container groups, as it was | $68.88 | Each site has its own processor and its own 1.5 GB; one site restarting does not touch the other | Twice the price of the image it runs, at a fifth of one processor each |
| One container group answering both names | $34.44 | Half the bill with no new service | The application has to choose its default store from the name or the port a request arrived on, which is a code path that exists only to save money, and one group is still 1 vCPU granted and mostly idle |
| **One App Service plan, Linux B1, two web apps** | **$12.41** | Always On, a health-check path the platform acts on, log streaming, managed certificates if the edge is ever retired, HTTPS from the edge to the origin, and the shape `infra/main.bicep` has described since day one | One machine carries both sites, so a plan restart takes both; 1 vCPU and 1.75 GB are shared |
| The same plan at B2 | $24.82 | 2 vCPU and 3.5 GB shared, if the memory says B1 is too small | Twice B1 for headroom the readings may not need |
| Standard S1, for deployment slots | $58.40 | A roll with no restart the visitor can see | Nearly the old bill, for a site whose rolls already restart |
| Free F1 | $0.00 | Nothing to pay | Rejected: 60 processor-minutes a day and no Always On, on a process that holds a hundred thousand vehicles in memory and samples itself every fifteen seconds |

**Decision: one Linux B1 plan, two web apps for containers, the same image and the same identity.
B2 only if the memory measured on the plan says so.**

## The wall, measured again, and the region it moved the plan to

The day-one record (ADR-004) measured App Service refusing this subscription outright:
`SubscriptionIsOverQuotaForSku`, a limit of zero machines at B1 and F1. That was three weeks ago and
the subscription is the same class it was then, so before this record was written as a decision
Azure was asked to validate, not create, a Linux plan at four sizes in seven regions.

West US 2, where the containers and the document store are, still answers zero at F1, B1, B2 and
P0v3. So do West US, South Central US, East US and East US 2. **West US 3 and Central US validate
every size.** The wall is the region and not the subscription.

West US 3 is where the relational server already is, for the same reason: West US 2 refused to
create it (ADR: The SQL Server backend). So the plan goes to West US 3, at the same $0.017 an hour,
and the move changes which store pays for distance. Until now the container sat beside the document
store and one region from the relational one, 2 ms against 39 ms a round trip
(ADR: The same performance, proven). On the plan it sits beside the relational store and one region
from the document store. Neither engine changed; the region boundary moved to the other side of the
comparison, and the section below carries what it measured rather than what that sentence predicts.

The catalogue is unaffected either way. A hundred thousand vehicles are read into the process at
start and served from memory, so a listing, a filter and a vehicle page never cross a region at all.

## What the plan measured

The plan and both sites were created at 04:07 CDT on 20 September from `infra/appservice.bicep`, in
incremental mode, running the image the container groups were running, and nothing pointed a public
name at them until the readings below were in.

**The origins answered.** Both sites came up healthy on both stores in about a minute, each one's
own page sweep read 115 of 115 addresses up, the machines card read the container's memory and 240
rows of the relational store's own view, and each site's comparison card read the other. One card
did not: Azure's view of the container answered "unavailable", which is the different door named
below, and is what this version fixes.

**The distance moved to the other store, as predicted.** The health page times one probe of each
store, two statements or two point reads. From the container groups in West US 2 it read 77 to
79 ms for the relational store and 3 to 6 ms for the document store. From the plan in West US 3 it
reads 7 to 28 ms for the relational store and 80 to 110 ms for the document store. Five reads of
each, same minute, same stores.

**The memory said B1, with one setting changed.** With both sites warming both stores, which is what
the container groups did, the plan's own metric read 85 to 91 per cent of its memory at rest and 94
during a roll, and each process's working set was pressed from 440 MB down to about 250 MB, below
the size of its own managed heap, which is a machine paging. Nothing failed: a roll of one site was
watched every fifteen seconds for four minutes, the old container kept answering until the new one
was warm, and every read of both sites answered 200. But single reads took up to four seconds while
it happened, and a machine that pages at rest has nothing left for a busy hour.

Each site holds two catalogues only because `Store:WarmOthers` warms the store it does not serve,
and that setting was a deploy-time choice made when each site had 1.5 GB to itself
(ADR: One container, both stores). On the plan it is off, which is the application's default
everywhere else: each site warms the store it serves and the other warms on first use, which today
means the first proof run after a roll. The managed heap went from about 260 MB to 130 and 155 MB a
site, the working sets settled near 300 MB and stayed above their heaps, and the plan's metric came
down to 80 to 85 per cent. That metric counts everything on the machine, the platform's own
processes included, and it is the working sets staying above their heaps that says the sites fit.
B2 is one parameter, `skuName`, and $12.41 a month more, if a busier month says so.

## What is bought beyond the money

- **Always On**, so the platform keeps the process warm rather than the first visitor of the hour.
- **A health-check path.** App Service asks `/healthz` on a clock and replaces an instance that stops
  answering. The container groups had a Docker HEALTHCHECK that nothing acted on.
- **HTTPS from the edge to the origin.** The container groups answered plain HTTP on port 8080 and
  the hop from Netlify to Azure was the one unencrypted link in the chain
  ([Hosting](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md)). `azurewebsites.net` answers on a
  certificate Azure manages, and the two web apps accept HTTPS only.
- **Log streaming and a managed certificate for a custom name**, neither used today, both there the
  day the edge is retired.
- **The template stops describing a plan nobody runs.** `infra/main.bicep` has described App Service
  since day one and had never been deployed. This is that design without Front Door, which the
  subscription still refuses.

## What is sold

- **One machine carries both sites.** A plan restart, a platform patch or a memory ceiling takes both
  at once. They were independent before; they share a fate now. For two faces of one demonstration
  that is the right trade, and it would be the wrong one for two products.
- **1.75 GB is shared**, where each group had 1.5 GB to itself. Two processes at about 580 MB fit on
  paper, and paper is not where it is decided: the section below is the reading taken on the plan.
- **No deployment slots.** Slots start at Standard, $58.40 a month, and are not bought. A roll
  restarts the site, as a roll restarted the container group.
- **The identity's token comes from a different door.** Container Instances serves managed identity
  tokens from the instance metadata address; App Service serves them from an endpoint it names in
  two environment variables. The Azure SDK knows both. The two readings this application takes by
  hand, its own resource and its telemetry, had to learn the second.

## What does not change

The identity. Both web apps carry `id-theyard-ss`, the user-assigned identity the container groups
carried, and its client id is what the database user was created from
(ADR: The SQL Server backend). So there is no database change at all: the contained user, its two
roles and the `VIEW DATABASE STATE` grant all keep working, the registry pull rides the same
`AcrPull`, the document store the same data-plane role, the mail the same role on the communication
service. The public addresses do not change either. Two lines in `edge/_redirects` point the same two
names at two new origins.

## The way back

The container groups are stopped, not deleted, for a week. A stopped group bills nothing and starts
with one command. `az container start` on both groups and the two old lines back in
`edge/_redirects` puts the site exactly where it was on 19 September, inside five minutes, at any
point in that week. Deleting them afterwards is a separate act, on the owner's word.

## Files

- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): the plan and the two sites, what differs between them, and every setting the container groups carried.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the two container groups, stopped and kept, which is the way back.
- [`api/TheYard.Api/IdentityTokens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/IdentityTokens.cs): a managed identity token from whichever door the host has.
- [`api/TheYard.Api/Observability.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Observability.cs): the site asking Azure about itself, as a container group or as a web app on a shared plan.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/deploy-cosmos.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-cosmos.yml): a roll sets the image and the three values the repository does not hold.
- [`api/TheYard.Tests/AzureSelfTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AzureSelfTests.cs) and [`api/TheYard.Tests/AppServiceTemplateTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AppServiceTemplateTests.cs): both doors, both shapes, and the template held to the files it took over from.
- [`docs/ADR-004-deployment-pivots.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-004-deployment-pivots.md): the day-one wall this record measured again.

```live path=infra/appservice.bicep region=two-sites
```

```live path=api/TheYard.Api/IdentityTokens.cs region=identity-token
```
