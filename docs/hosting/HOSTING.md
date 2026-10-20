# Hosting

TheYard is on the internet at https://theyard.stevenstout.biz. This page is the
parent record for how that works, written for a reviewer or anyone
learning how a small production setup fits together. The records under it in
this menu are its children: read this page for the shape, open a child for the
full reasoning behind one decision. Everything, the infrastructure code
included, is served from these menus; nothing requires opening the repository.

## In plain words

This page explains how the site reaches the internet: a domain name and a free edge service (Netlify) that handles the secure connection, in front of two web apps on one Azure machine (an App Service plan): one that runs both sites and The Shed in a single process and answers each by the name typed, and a rendering service that draws every page before it leaves. The piece still waiting is a production edge (Azure Front Door) that the free trial refuses to create.

In practice, a developer sees how a small production setup fits together, with its infrastructure code served on the site, and the organization runs it on free services and one small plan while the full design waits behind one parameter.

## The picture

[![TheYard infrastructure: a request from the browser through Wix DNS and the Netlify edge to the rendering service and the site's web app on the App Service plan on Azure; a green gate's push through the two deploys to the registry and the roll; and Azure Front Door, designed, parameterized and still refused](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/infrastructure.png)](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)

*A preview. [Open the infrastructure diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)
to zoom in and follow it; every diagram on this site opens that way (ADR: Diagram pages).*

Three lanes: a request from left to right, through the rendering service
and the API it reads, a merge becoming a roll, and the one piece of the
production design that still waits for a subscription upgrade. Every name in it is
the one the records and the pipeline logs carry. The source is
[`docs/images/infrastructure.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/infrastructure.svg); the records below explain each box.

Since 1.0.0.100 there are two sites behind that edge, and since 19 October
they and The Shed are one web app. The second drawing is how the names reach
it, the two Yard names through the edge and The Shed's straight to Azure, how a
page reaches the rendering service beside it, and how the one process reaches
both stores (ADR: A permanent address for the second site; ADR: One web app for
every .NET process):

[![TheYard's two sites and The Shed: four names at Wix, one Netlify edge, one web app for every .NET process and the rendering service on one App Service plan, both stores behind it](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/two-sites.png)](https://theyard.stevenstout.biz/api/docs/diagrams/two-sites)

*A preview. [Open the two-sites diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/two-sites)
to zoom in and follow it.*

## Websites and resources used

- **Azure (portal.azure.com).** Runs the app: one Linux B2 App Service plan
  for the compute, carrying two web apps for containers: since 19 October one
  that runs both sites and The Shed in one process (ADR: One web app for every
  .NET process; from 1.0.0.156 each site had its own, ADR: One plan, two sites),
  and since 1.0.3.103 the rendering service that draws every page for both
  sites (ADR: A rendering service beside the API); Container Registry for the
  two images;
  Azure SQL Database for the store the first site is on and Azure Cosmos DB
  for the second's, with both stores opened by both sites since 1.0.0.94
  (ADR: One container, both stores). The only place code executes. For its
  first three weeks the compute was Azure Container Instances, one container
  group per site.
- **Wix (wix.com).** The domain registrar. Holds stevenstout.biz and answers
  DNS; two CNAME records, theyard and theyard-cosmos, point the two sites at
  the same edge, and a third, theshed, points The Shed at the web app itself.
- **Netlify (netlify.com).** The free edge. Terminates HTTPS for both names
  on one certificate, and forwards every request to Azure over HTTPS: a page
  to the rendering service, everything else to the one web app with the name
  the visitor typed in an `X-Yard-Host` header (ADR: A permanent address for the
  second site; ADR: One web app for every .NET process).
- **Let's Encrypt (letsencrypt.org).** Issues the certificate at no cost;
  Netlify renews it automatically.
- **Cloudflare (cloudflare.com).** Configured and dormant; becomes the edge
  after the domain transfers registrars, around late October 2026.
- **GitHub (github.com/SteveStout/TheYard).** Holds the code, runs the deploys
  when the ship's gate pushes, runs the test wall on a pull request, and feeds the edge
  deploys.

## The chain, request by request

1. **DNS.** theyard.stevenstout.biz and theyard-cosmos.stevenstout.biz are
   two CNAME records at the registrar (Wix), both pointing at the same edge.
   theshed.stevenstout.biz is a CNAME straight to the web app's own
   `azurewebsites.net` name, with App Service's managed certificate.
   TTLs sit at 30 minutes to an hour while the setup is young so changes
   propagate fast.
2. **Edge.** Netlify's free tier terminates HTTPS for the two Yard names. The
   name a request arrived on picks the rules, and the path picks the origin: a
   page, the bare path with its query, goes to the rendering service under
   that site's name, and everything else, the bundle, the photographs and
   `/api`, goes to the one web app with `X-Yard-Host` saying which name was
   typed (ADR: A permanent address for the second site; ADR: A rendering
   service beside the API).
3. **Origin.** One App Service plan, `PLAN-THEYARD-SS`, Linux B2 in West US 3,
   runs two web apps for containers in RG-THEYARD-SS: the API's image once, in
   APP-THEYARD-ONE-SS, which answers all three names in one process, and the
   rendering service's image once, which draws a page for either site by
   reading the API with the site's store named. Azure does all the compute;
   the edge only forwards. Each Yard site is one store's site by its name, and
   the Store bar at the top of every page links to the other at the same page
   (ADR: One container, both stores; ADR: One web app for every .NET process).
   Why one plan and why West US 3 are ADR: One plan, two sites, which also
   records the move from B1 to B2 when the rendering service arrived.

## How a request gets answered

Three domains, two web apps, one process for everything .NET. Follow a request
from the address bar to the code that answers it, every step read from this
build. The whole path first, then each hop with its code:

[![How a request gets answered: three names at Wix, the Netlify edge's two files, the rendering service, and inside the one web app SiteHosts picking the name, ShedMount handing The Shed's name to The Shed, and Stores picking Azure SQL or Cosmos DB](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/request-path.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/request-path)

*A preview. [Open the request path in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/request-path) to zoom in. Drawn by [`docs/images/request-path.mjs`](https://github.com/SteveStout/TheYard/blob/main/docs/images/request-path.mjs).*

**theyard.stevenstout.biz and theyard-cosmos.stevenstout.biz, at the edge.**
Netlify reads `edge/_redirects` first. A page (the bare path, with its query)
goes to the rendering service as that site; the bare domain and www move to
theyard with a 301:

```live path=edge/_redirects region=rules
```

Everything else falls through to `netlify.toml`. Both rules reach the same web
app, so each says which name the visitor typed; the Cosmos DB site's rule comes
first, because the catch-all would match it too:

```live path=netlify.toml region=proxy-rules
```

**theshed.stevenstout.biz, with no edge.** The name is bound to the web app in
Azure, and App Service's front end routes it there by the Host header. No rule
in this repository is involved.

**Inside the web app: which name.** One class knows the three names and which
one a request arrived on. The Host header first, when App Service routed the
request by one of the three names; only on any other Host, which is the web
app's own address where the edge sends both Yard names, the edge's
`X-Yard-Host`, and only when it names one of the three. A sender can choose
between the three sites and nothing else:

```live path=api/TheYard.Api/SiteHosts.cs region=site-hosts
```

**The Shed's name goes to The Shed.** Before anything of TheYard's pipeline sees
it, a request on The Shed's name is handed to The Shed's own application,
started inside the same process from its own published folder. The mount sits
in `Program.cs` right after the app is built, so it runs ahead of TheYard's
whole request pipeline:

```live path=api/TheYard.Api/Shared/Sites/HostedSiteMount.cs region=hosted-sites
```

The application it hands the request to is The Shed's own: its builder, its
registrations, then its request pipeline (`samples/theshed/Composition/RequestPipeline.cs`)
and its controllers (`samples/theshed/Controllers`), on a server with no socket:

```live path=api/TheYard.Api/Shared/Sites/TheShed/ShedApplication.cs region=shed-application
```

**A Yard name picks the store.** A request that names a store with
`X-Yard-Store` gets it, which is how the proof card and the measurements ask for
one; otherwise the name picks it, and a request on no name (a health probe, the
deploy's first read) gets the relational store. Every endpoint then asks
`CurrentBackend`, resolved once per request from that answer, and awaits the
store's warmth before it reads:

```live path=api/TheYard.Api/Stores.cs region=backends
```

**The rendering service calls back.** The edge sends a page to it as
`/site/sql` or `/site/cosmos`; `render/server.mjs` looks the name up in
`YARD_SITES`, where both names are the one web app's address, and the name is
also the store's key. Every read then names that store, because both sites
share one address:

```live path=render/loaders.ts region=reads
```

**What sets the names.** The template's one web app, its three names and The
Shed's folder:

```live path=infra/appservice.bicep region=sites-list
```

## The certificate

Let's Encrypt at the edge, issued and renewed automatically, one certificate
for all four names (the bare domain, www, theyard and theyard-cosmos).
Nothing was purchased and nothing expires by surprise. The edge-to-origin hop was plain
HTTP while the origin was a container group with no TLS listener; since 1.0.0.156 it is
HTTPS, on the certificate Azure manages for `azurewebsites.net`, so both legs are
encrypted: the browser to Netlify, and Netlify to the origin. The Shed's name has
one leg, the browser to App Service, on its managed certificate.

## Why not Front Door today

The free trial refuses to create it, and that was measured rather than assumed
(see ADR: Deployment strategy). The registrar also refuses nameserver changes, which rules out
Cloudflare's free tier until the domain can transfer, earliest late October
2026. The pattern survived both walls. Only the vendor is temporary.

## What is left of the production design

Open Infrastructure (Bicep) in this menu. Until 20 September 2026
infra/main.bicep described a design nobody ran: App Service behind Azure
Front Door with the origin locked. The App Service half of it runs now, and
the file is the description of what runs: the plan, the one web app, every
setting, the identity and the registry pull. It is deployed in incremental
mode only, because the same resource group holds the databases, the registry
and the identity, and none of them is in a template on purpose (ADR: One
plan, two sites).

The rendering service's web app is the same kind of description, in
`infra/render.bicep`, on the same plan (ADR: A rendering service beside the
API). This is the whole of it: the image, the two sites it reads by name from the one web app, the
deadline its reads run under, and the heap ceiling that turns a leak into a
restart instead of the plan's memory:

```live path=infra/render.bicep region=render-site
```

What is left is Front Door and the origin lock, behind one parameter that
defaults off. The origins are reachable directly today, as the container
groups were, and what that does and does not expose is on the Security page.
When the subscription allows Front Door it is one parameter, and the domain
layer means the public URL does not change in the switch, as it did not
change in this one.

## Files

- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep) and [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): what runs, the plan and
  the one web app (served above as Infrastructure (Bicep)).
- [`infra/render.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/render.bicep): the rendering service's web app, on the same plan.
- [`render/Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/render/Dockerfile): the rendering service's image: the build on Node, nothing else.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/22b3015/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/22b3015/infra/aci-theyard-cosmos.yaml): the two
  container groups that ran until 1.0.0.156, stopped and kept as the way back; the files retired on 19 October with the per-site settings they carried (ADR: One web app for every .NET process), and the links are to the last commit that held them.
- [`infra/cosmos/`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): the container definitions the second store is built from.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the HTTPS edge.
- [`.github/workflows/edge-branch.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/edge-branch.yml): moves the `edge` branch, which Netlify builds from, to main only when `edge/` or `netlify.toml` changes and only after Deploy has verified the origin the rules name, then reads both Yard names.
- [`api/TheYard.Api/SiteHosts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/SiteHosts.cs) and [`api/TheYard.Api/Shared/Sites/HostedSiteMount.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Sites/HostedSiteMount.cs): which name a request arrived on, and The Shed's name to The Shed.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image the one web app runs, TheYard and The Shed.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml) and [`.github/workflows/deploy-render.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy-render.yml): how a merge becomes a roll of the sites, and of the service.
