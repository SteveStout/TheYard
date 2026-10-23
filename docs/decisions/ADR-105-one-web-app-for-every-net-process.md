# ADR: One web app for every .NET process

Status: accepted, 2026-10-19. The SQL site, the Cosmos DB site and The Shed run in one process in one web app, APP-THEYARD-ONE-SS, on the same B2 plan, and each request is answered by the name it arrived on. The three web apps they ran in are deleted: APP-THEYARD-SS and APP-THEYARD-COSMOS-SS at 15:52 and 15:53 UTC on Steve's word of the same morning, and APP-THESHED-SS at 19:36 once The Shed's DNS record named the new web app. The cutover, its times and the memory before and after are in the addendum below. The rendering service stays a web app of its own.

## Reading list

For Steve, when he is fresh, in this order:

1. [ADR: One plan, two sites](https://theyard.stevenstout.biz/?doc=adr-one-plan): the shape this replaces, and why it was right when it was made.
2. The plan card on the Admin tab, its last row: the memory the plan holds outside every app, which is the number this record moves.
3. Microsoft Learn, [Host ASP.NET Core in a web farm and behind proxies](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer): why a request's own Host header is not always the name the visitor typed.
4. Microsoft Learn, [Routing in ASP.NET Core, host matching](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing#host-matching-in-routes-with-requirehost): the framework's own answer to one app on several names.

## In plain words

Three of the four small programs on the plan were each running on their own rented computer slot, and each slot costs memory just for existing, whether anyone visits or not. The plan they share had more memory going to that overhead than to the programs themselves. So the three now run as one program in one slot. It looks at the address a visitor typed and answers as the right site: the SQL site, the Cosmos DB site, or The Shed. Nothing a visitor sees changes, and splitting them apart again later is one file and one rule.

## Context

Steve, 19 October, 07:47 to 07:49 CDT: "since I've demoed the shed, let merge all .net into one new app service, and delete the old", and for the records, "on the existing ADRs indicate the separate app services would be for increased load and separation down the road but we merged due to low traffic on the update and ram".

The separate web apps were the design for load and separation: one process per site and per product, so one could roll, fail or be scaled without the others (ADR: One plan, two sites; ADR: A permanent address for the second site; The Shed's ADR-001). The plan card's last row showed what that costs on a plan with little traffic. It is the plan's memory in use minus what each app reports for itself: the host, the platform's agent, the image layers and one Kudu sidecar per app. That row was larger than the four apps together, and fewer apps is the only lever on it. Each .NET site already ran both stores in its own process (ADR: One container, both stores); the two differed in four settings.

## Decision

**One image, one process, three names.** The image carries TheYard and The Shed. The process answers `theyard.stevenstout.biz`, `theyard-cosmos.stevenstout.biz` and `theshed.stevenstout.biz` (the `Hosts` settings). Which name a request arrived on is its Host header when App Service routed it by one of the three names, which is how The Shed's domain arrives, bound straight to the web app; the two Yard domains arrive through the edge, which proxies both to the web app's own address and says which domain the visitor typed in `X-Yard-Host`, read only when the Host is not one of the three. A header counts only when it names one of the three configured names, so the most a sender can choose is which of the three sites answers.

```live path=api/TheYard.Api/Shared/Instance/SiteHosts.cs region=site-hosts
```

**The store follows the name.** A request that names no store gets the store of its site; the `X-Yard-Store` header still wins, which is how the proof and the measurements ask for a store. `Store:Default` is gone from the template: with no name at all (a health probe, the deploy's first read of the origin) the process answers with the relational store. Each store's catalogue is kept by this process, both of them, as the only process that keeps them (ADR: The store does the work). The other site's address on the store bar comes from the names, so the link from one site to the other still goes to the other domain.

```live path=api/TheYard.Api/Shared/Stores/Stores.cs region=backends
```

**The Shed is its own application, started inside this process.** The Shed's Program.cs stays the way a reader starts it on their own machine. `ShedMount` starts the same application in this process: The Shed's own builder, its own four registrations, its own pipeline and controllers, in a service container of its own, with a server that has no socket where Kestrel would be. A request on The Shed's name is handed to that application before anything of TheYard's pipeline sees it. Starting it whole, rather than registering its services on TheYard's builder, removes every collision between the two service containers by construction: its Kestrel body limit configures a server that never listens, and its upload limit is set on each of its own requests instead; its rate limiter, exception handler and snake_case JSON stay in its container; its `/healthz` is reached only on its name; its documents, sample home and live sources are read from its own published folder, `/app/shed`, which `Shed__ContentRoot` names. What the two still share is the process: one memory limit, one set of environment variables (each reads only its own settings), and the framework's process-wide telemetry sources, so Application Insights may see a Shed request at both layers. Its lifetime is TheYard's: it answers no signal of its own and is stopped when TheYard stops. A Shed that cannot start answers its name with a 503 and leaves the Yard names running; a failure past The Shed's own error handler is logged in TheYard's log.

```live path=api/TheYard.Api/Shared/Sites/HostedSiteMount.cs region=hosted-sites
```

**The peer card is retired with a sentence.** It read the other web app's metrics; the other site is now this process, and the comparison card already sets the two stores side by side. With no `Peer__Url`, the card says exactly that (an addendum on ADR: Backends, side by side).

**The renderer names the store.** Both sites' pages are read from the one web app's address, so every read the renderer makes carries `X-Yard-Store` with the site it draws for. It stays a web app of its own: Node is its own runtime and its own roll (ADR: A rendering service beside the API).

**The edge moves after the origin it names is verified.** The two Yard domains' proxy rules moved from `edge/_redirects` to `netlify.toml`, the only place a rule can add a header to what it proxies. The Edge branch workflow now starts when Deploy succeeds and only then moves the edge, then reads both names until each serves the build; before, it moved on the push itself, which on this change would have sent visitors to a web app still starting.

**Deploy rolls one web app.** `deploy.yml` builds the image, rolls APP-THEYARD-ONE-SS and, before any domain, reads the Cosmos DB site's name and The Shed's name at the origin with `X-Yard-Host`: the first must get Azure Cosmos DB with no store named, the second The Shed's own health check and page. Deploy Cosmos and Deploy The Shed retired with their web apps, and so did the two container group files and The Shed's own template.

## What was considered

- **The Shed's services on TheYard's builder, routed with `RequireHost` and a host-conditioned `MapWhen` for its files.** The framework's own answer to one app on several names, and the merge brief's first design. Rejected because it puts two applications in one service container: The Shed's Kestrel body limit would apply to TheYard, its exception handler and snake_case JSON settings would reach TheYard's endpoints, and its rate limiter policies would share a namespace with TheYard's. Each could be scoped by hand; starting The Shed as its own application removes them all.
- **The Shed in a container beside TheYard in the same web app (a sidecar).** Keeps two processes, which is the memory this change is for.
- **A smaller second plan for The Shed.** Costs money the project does not spend, and the plan's outside-the-apps share goes with it.
- **Keep the three web apps.** Right for load; the plan card's last row says this plan has none to speak of.

## Consequences

- One process holds both catalogues' upkeep, both stores' connections and The Shed. A failure that takes the process down takes all three names down; before, it took one. The health check, the roll and the readiness wait are one each instead of three.
- One roll for everything: a change to The Shed rolls TheYard and the renderer too, and CI runs on a push that touches only The Shed, because The Shed is built into the app.
- The kept rings and the machines history are this process's, so the Admin tab on either Yard name reads the same history; it was two histories of two processes before.
- The split is one entry in `infra/appservice.bicep`'s `sites` list and one rule in `netlify.toml` away when load returns: a second web app from the same image with its own `Hosts` values, and that domain's rule pointed at it. The Shed's own domain is the exception: its binding and certificate are set in Azure, not in a template, and move by hand, as they did on 19 October.

## Addendum, 2026-10-19: the cutover, as it happened

Read live through the day from the operator's PC; every time is UTC.

| when | what |
| --- | --- |
| 13:46 | Before: the plan at 72 per cent, 2.52 of 3.5 GB; the SQL site 404 MB, the Cosmos DB site 370, the rendering service 105, The Shed 98, together 977 MB; the plan outside the apps 1,603 MB |
| 13:53 | APP-THEYARD-ONE-SS created from `infra/appservice.bicep` on the plan, on the 1.0.3.183 image with the SQL site's three secret values read across; the only new resource |
| 14:28 | 1.0.3.184 on main (30ee0dc) after its second gate run. The first was red on Prettier for three files under `render/` and on one Cosmos DB bid test that read a cached minimum; both fixed |
| 14:34 | Deploy #367 built and pushed the image and was refused at Roll the site: the GitHub deploy identity holds Website Contributor on each web app it rolls, and none on the new one. The grant is the owner's to give |
| 15:21 | The new web app rolled to 1.0.3.184 from the operator's PC instead; at its own address the Cosmos DB site's name got Azure Cosmos DB, the SQL site's got Azure SQL Database, and The Shed's name got The Shed's page, health, version and search |
| 15:22 | The edge moved (Edge branch, run by hand once the origin was verified); both Yard domains served 1.0.3.184 eleven seconds later |
| 15:25 to 15:35 | The rendering service pointed at the new web app; pages drawn on both sites again |
| 15:35 to 15:36 | The Shed's name moved to the new web app with its existing certificate: 5 of 206 reads every 2 seconds failed, all between 15:36:05 and 15:36:38 |
| 15:43 | Every public page on the three domains read green: 33 checks, the Store bar both ways, the proof, plan and peer cards |
| 15:44 | The three old web apps stopped; 15:46 every check green again |
| 15:52 and 15:53 | APP-THEYARD-SS and APP-THEYARD-COSMOS-SS deleted. The plan, the databases, the registry, the identity and the rendering service untouched |
| 15:54 | The plan at 60 per cent, 2.10 of 3.5 GB: the one web app 388 MB, the rendering service 137 MB |
| 16:54 | An hour after the delete, the number for the record: the plan at 61 per cent, 2.14 of 3.5 GB; the one web app 434 MB, the rendering service 139 MB, The Shed's old web app stopped at 0; the plan outside the apps 1,613 MB. Azure Monitor's own five-minute averages read 59.0 to 61.6 per cent from 15:54 to 16:49 |
| 18:14 | 1.0.3.185 rolled to the one web app from the operator's PC; both Yard domains on it at 18:16, and 33 checks green at 18:18 |
| 18:48 | The owner gave the GitHub deploy identity Website Contributor on the one web app. Deploy for 1.0.3.185, rerun at 19:14, passed its roll on its own: every roll from here is the workflow's again |
| 18:56 | The Shed's CNAME at Wix changed to the one web app, its TTL to 30 minutes. The record had been served for an hour at a time, so resolvers held the old answer for up to that long |
| 19:36 to 19:37 | Both public resolvers named the one web app for The Shed at 19:36; The Shed answered healthy at its own name from it, and APP-THESHED-SS was deleted at 19:36:54. 33 checks green at 19:37; the plan at 59 per cent, 2.07 of 3.5 GB, two apps on it: the one web app 437 MB, the rendering service 95 MB, outside them 1,583 MB |

What moved and what did not. The plan went from 72 to 61 per cent, 2.52 to 2.14 GB, and all of it came from the apps: 977 MB for four became 573 MB for two, because the one process holding both sites and The Shed (434 MB) is far less than the three it replaced (876 MB). The row outside the apps, the one this record was argued from, did not shrink in the hour: 1,603 MB before, 1,613 MB after. So the share the platform holds outside the apps is not one sidecar per app in any way an hour shows, and the saving is the apps' own: two .NET runtimes and their caches that no longer exist. The record keeps the argument it was made on and this reading beside it; the outside row is worth reading again a day on.

The Netlify edge read 782 credits left and 212 used in the billing period before its one deploy for this change.

## Where it sits

The host ring only, and the deploy around it. Domain, Application and Infrastructure are unchanged: a store is still chosen per request in the host, by the `X-Yard-Store` header, else by the request's name, where until now it was the header, else a container setting. The Shed's rings are its own and untouched; TheYard's host references its assembly and starts it. Single responsibility held at the file level: `SiteHosts` knows the names, `Backends` picks the store, `ShedMount` routes The Shed's name and `ShedApplication` runs it. The cost is blast radius, said under Consequences.

## Addendum, 2026-10-20 (1.0.3.190): the names are a list, The Shed is one hosted site

From 1.0.3.190 the names this process answers to are one configured list, `Sites:0:Host` and `Sites:0:Store` and so on, where a site with no store is a guest application this process hosts (`Shared/Wiring/SiteSettings.cs`). `SiteHosts` and `Backends` are lookups over it and name no store and no host, which `SliceTests` holds with a scan of their string literals; a third site or a third store is a setting. The template writes the list; a web app configured before it carries `Hosts__Sql`, `Hosts__Cosmos` and `Hosts__Shed`, which read as the same three sites, so a roll onto the old settings keeps every name. The Shed stands behind `IHostedSite` (`Shared/Sites/IHostedSite.cs`): `ShedSite` is its one implementation, started by `HostedSiteMount` once the host is built and disposed by `HostedSiteStopper`, the last hosted service, after the drain. `OnionTests` now fails when any type but `ShedSite` and `ShedApplication` uses The Shed's assembly (ADR: The onion cut into vertical slices, rules 3 and 5).

## Files

- [`docs/images/request-path.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/request-path.mjs) - the drawing of every way a request reaches code, on the Hosting page
- [`api/TheYard.Api/Shared/Instance/SiteHosts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Instance/SiteHosts.cs) - the three names and which one a request arrived on
- [`api/TheYard.Api/Shared/Stores/Stores.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Stores/Stores.cs) - the store a request gets
- [`api/TheYard.Api/Shared/Sites/HostedSiteMount.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Sites/HostedSiteMount.cs) - The Shed's name to The Shed
- [`api/TheYard.Api/Shared/Sites/TheShed/ShedApplication.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Sites/TheShed/ShedApplication.cs) - The Shed's application in this process, and the server with no socket
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep) - the plan and the one web app
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects) - the edge
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/edge-branch.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/edge-branch.yml) - the roll, then the edge
- [`api/TheYard.Tests/SiteHostsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SiteHostsTests.cs) - the rule, and The Shed mounted in the test host
