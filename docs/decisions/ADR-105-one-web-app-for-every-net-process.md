# ADR: One web app for every .NET process

Status: accepted, 2026-10-19. The SQL site, the Cosmos DB site and The Shed run in one process in one web app, APP-THEYARD-ONE-SS, on the same B2 plan, and each request is answered by the name it arrived on. The three web apps they ran in, APP-THEYARD-SS, APP-THEYARD-COSMOS-SS and APP-THESHED-SS, are retired: their names move to the new web app, and then the three are deleted on Steve's word of the same morning. The times and the memory before and after go in the addendum below as they are read. The rendering service stays a web app of its own.

## Reading list

For Steve, when he is fresh, in this order:

1. [ADR: One plan, two sites](https://theyard.stevenstout.biz/?doc=adr-one-plan): the shape this replaces, and why it was right when it was made.
2. The plan card on the Admin tab, its last row: the memory the plan holds outside every app, which is the number this record moves.
3. Microsoft Learn, [Host ASP.NET Core in a web farm and behind proxies](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer): why a request's own Host header is not always the name the visitor typed.
4. Microsoft Learn, [Routing in ASP.NET Core, host matching](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing#host-matching-in-routes-with-requirehost): the framework's own answer to one app on several names.

## In plain words

Three small programs were each running on their own rented computer slot, and each slot costs memory just for existing, whether anyone visits or not. The plan they share had more memory going to that overhead than to the programs themselves. So the three now run as one program in one slot. It looks at the address a visitor typed and answers as the right site: the SQL site, the Cosmos DB site, or The Shed. Nothing a visitor sees changes, and splitting them apart again later is one file and one rule.

## Context

Steve, 19 October, 07:47 to 07:49 CDT: "since I've demoed the shed, let merge all .net into one new app service, and delete the old", and for the records, "on the existing ADRs indicate the separate app services would be for increased load and separation down the road but we merged due to low traffic on the update and ram".

The separate web apps were the design for load and separation: one process per site and per product, so one could roll, fail or be scaled without the others (ADR: One plan, two sites; ADR: A permanent address for the second site; The Shed's ADR-001). The plan card's last row showed what that costs on a plan with little traffic. It is the plan's memory in use minus what each app reports for itself: the host, the platform's agent, the image layers and one Kudu sidecar per app. That row was larger than the four apps together, and fewer apps is the only lever on it. Each .NET site already ran both stores in its own process (ADR: One container, both stores); the two differed in four settings.

## Decision

**One image, one process, three names.** The image carries TheYard and The Shed. The process answers `theyard.stevenstout.biz`, `theyard-cosmos.stevenstout.biz` and `theshed.stevenstout.biz` (the `Hosts` settings). Which name a request arrived on is its Host header when App Service routed it by that name, which is how The Shed's domain arrives, bound straight to the web app; the two Yard domains arrive through the edge, which proxies both to the web app's own address and says which domain the visitor typed in `X-Yard-Host`. A header counts only when it names one of the three configured names, so the most a sender can choose is which of the three sites answers.

```live path=api/TheYard.Api/SiteHosts.cs region=site-hosts
```

**The store follows the name.** A request that names no store gets the store of its site; the `X-Yard-Store` header still wins, which is how the proof and the measurements ask for a store. `Store:Default` is gone from the template: with no name at all (a health probe, the deploy's first read of the origin) the process answers with the relational store. Each store's catalogue is kept by this process, both of them, as the only process that keeps them (ADR: The store does the work). The other site's address on the store bar comes from the names, so the link from one site to the other still goes to the other domain.

```live path=api/TheYard.Api/Stores.cs region=backends
```

**The Shed is its own application, started inside this process.** The Shed's Program.cs stays the way a reader starts it on their own machine. `ShedMount` starts the same application in this process: The Shed's own builder, its own four registrations, its own pipeline and controllers, in a service container of its own, with a server that has no socket where Kestrel would be. A request on The Shed's name is handed to that application before anything of TheYard's pipeline sees it. Starting it whole, rather than registering its services on TheYard's builder, removes every collision by construction: its Kestrel body limit configures a server that never listens, and its upload limit is set on each of its own requests instead; its rate limiter, exception handler and snake_case JSON stay in its container; its `/healthz` is reached only on its name; its documents, sample home and live sources are read from its own published folder, `/app/shed`, which `Shed__ContentRoot` names.

```live path=api/TheYard.Api/Composition/ShedMount.cs region=shed-mount
```

**The peer card is retired with a sentence.** It read the other web app's metrics; the other site is now this process, and the comparison card already sets the two stores side by side. With no `Peer__Url`, the card says exactly that (an addendum on ADR: Backends, side by side).

**The renderer names the store.** Both sites' pages are read from the one web app's address, so every read the renderer makes carries `X-Yard-Store` with the site it draws for. It stays a web app of its own: Node is its own runtime and its own roll (ADR: A rendering service beside the API).

**The edge moves after the origin it names is verified.** The two Yard domains' proxy rules moved from `edge/_redirects` to `netlify.toml`, the only place a rule can add a header to what it proxies. The Edge branch workflow now starts when Deploy succeeds and only then moves the edge, then reads both names until each serves the build; before, it moved on the push itself, which on this change would have sent visitors to a web app still starting.

**Deploy rolls one web app.** `deploy.yml` builds the image, rolls APP-THEYARD-ONE-SS and, before any domain, reads the Cosmos DB site's name and The Shed's name at the origin with `X-Yard-Host`: the first must get Azure Cosmos DB with no store named, the second The Shed's own health check and page. Deploy Cosmos and Deploy The Shed retired with their web apps, and so did the two container group files and The Shed's own template.

## Consequences

- One process holds both catalogues' upkeep, both stores' connections and The Shed. A failure that takes the process down takes all three names down; before, it took one. The health check, the roll and the readiness wait are one each instead of three.
- One roll for everything: a change to The Shed rolls TheYard and the renderer too, and CI runs on a push that touches only The Shed, because The Shed is built into the app.
- The kept rings and the machines history are this process's, so the Admin tab on either Yard name reads the same history; it was two histories of two processes before.
- The split is one entry in `infra/appservice.bicep`'s `sites` list and one rule in `netlify.toml` away when load returns: a second web app from the same image with its own `Hosts` values, and that domain's rule pointed at it.

## Where it sits

The host ring only, and the deploy around it. Domain, Application and Infrastructure are unchanged: a store is still chosen per request by the host, and the rule for which one moved from a container setting to the request's name. The Shed's rings are its own and untouched; TheYard's host references its assembly and starts it. Single responsibility held at the file level: `SiteHosts` knows the names, `Backends` picks the store, `ShedMount` routes The Shed's name and `ShedApplication` runs it. The cost is blast radius, said under Consequences.

## Files

- [`api/TheYard.Api/SiteHosts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/SiteHosts.cs) - the three names and which one a request arrived on
- [`api/TheYard.Api/Stores.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Stores.cs) - the store a request gets
- [`api/TheYard.Api/Composition/ShedMount.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Composition/ShedMount.cs) - The Shed's name to The Shed
- [`api/TheYard.Api/ShedApplication.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ShedApplication.cs) - The Shed's application in this process, and the server with no socket
- [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep) - the plan and the one web app
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects) - the edge
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/edge-branch.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/edge-branch.yml) - the roll, then the edge
- [`api/TheYard.Tests/SiteHostsTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/SiteHostsTests.cs) - the rule, and The Shed mounted in the test host
