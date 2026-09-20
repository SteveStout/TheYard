# Hosting

TheYard is on the internet at https://theyard.stevenstout.biz. This page is the
parent record for how that works, written for a hiring manager or anyone
learning how a small production setup fits together. The records under it in
this menu are its children: read this page for the shape, open a child for the
full reasoning behind one decision. Everything, the infrastructure code
included, is served from these menus; nothing requires opening the repository.

## The picture

[![TheYard infrastructure: a request from the browser through Wix DNS and the Netlify edge to a web app on the App Service plan on Azure; a merge through CI and Deploy to the registry and the roll; and Azure Front Door, designed, parameterized and still refused](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/infrastructure.png)](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)

*A preview. [Open the infrastructure diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/infrastructure)
to zoom in and follow it; every diagram on this site opens that way (ADR: Diagram pages).*

Three lanes: a request from left to right, a merge becoming a roll, and the
one piece of the production design that still waits for a subscription
upgrade. Every name in it is
the one the records and the pipeline logs carry. The source is
[`docs/images/infrastructure.svg`](https://github.com/SteveStout/TheYard/blob/main/docs/images/infrastructure.svg); the records below explain each box.

Since 1.0.0.100 there are two sites behind that edge, and the second
drawing is how the two names reach the two web apps on one plan and how both
sites reach both stores (ADR: A permanent address for the second site;
ADR: One plan, two sites):

[![TheYard's two sites: two names at Wix, one Netlify edge, two web apps on one App Service plan on Azure, both stores behind both](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/two-sites.png)](https://theyard.stevenstout.biz/api/docs/diagrams/two-sites)

*A preview. [Open the two-sites diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/two-sites)
to zoom in and follow it.*

## Websites and resources used

- **Azure (portal.azure.com).** Runs the app: one Linux B1 App Service plan
  for the compute, carrying two web apps for containers, one per site, since
  1.0.0.156 (ADR: One plan, two sites); Container Registry for the image;
  Azure SQL Database for the store the first site is on and Azure Cosmos DB
  for the second's, with both stores opened by both sites since 1.0.0.94
  (ADR: One container, both stores). The only place code executes. For its
  first three weeks the compute was Azure Container Instances, one container
  group per site.
- **Wix (wix.com).** The domain registrar. Holds stevenstout.biz and answers
  DNS; two CNAME records, theyard and theyard-cosmos, point the two sites at
  the same edge.
- **Netlify (netlify.com).** The free edge. Terminates HTTPS for both names
  on one certificate, and forwards every request to Azure unchanged, over
  HTTPS: theyard to the first web app, theyard-cosmos to the second (ADR: A
  permanent address for the second site).
- **Let's Encrypt (letsencrypt.org).** Issues the certificate at no cost;
  Netlify renews it automatically.
- **Cloudflare (cloudflare.com).** Configured and dormant; becomes the edge
  after the domain transfers registrars, around late October 2026.
- **GitHub (github.com/SteveStout/TheYard).** Holds the code, runs the test
  wall on every push, and feeds the edge deploys.

## The chain, request by request

1. **DNS.** theyard.stevenstout.biz and theyard-cosmos.stevenstout.biz are
   two CNAME records at the registrar (Wix), both pointing at the same edge.
   TTLs sit at 30 minutes while the setup is young so changes propagate fast.
   They get lengthened once things are boring.
2. **Edge.** Netlify's free tier terminates HTTPS and forwards every request
   unchanged. The entire edge is three files in this repository, deployed from
   GitHub on every push that touches them. The name a request arrived on
   picks the origin: two rules above the catch-all send theyard-cosmos to the
   second web app, and everything else goes to the first (ADR: A permanent
   address for the second site).
3. **Origin.** One App Service plan, `PLAN-THEYARD-SS`, Linux B1 in West US 3,
   runs the Docker image twice in RG-THEYARD-SS: two web apps for containers,
   each answering HTTPS on its own `azurewebsites.net` name and listening on
   port 8080 inside. Azure does all the compute. The edge only forwards. Both
   stores are opened by both sites since 1.0.0.94, and the second site runs
   the same image with Azure Cosmos DB as its default; since 1.0.0.100 it
   answers at https://theyard-cosmos.stevenstout.biz through the same edge
   and the same certificate. Each site is one store's site, and the Store bar
   at the top of every page links to the other at the same page (ADR: One
   container, both stores). Why one plan, why B1 and why West US 3 are
   ADR: One plan, two sites.

## The certificate

Let's Encrypt at the edge, issued and renewed automatically, one certificate
for all four names (the bare domain, www, theyard and theyard-cosmos).
Nothing was purchased and nothing expires by surprise. The edge-to-origin hop was plain
HTTP while the origin was a container group with no TLS listener; since 1.0.0.156 it is
HTTPS, on the certificate Azure manages for `azurewebsites.net`, so the chain is
encrypted end to end.

## Why not Front Door today

The free trial refuses to create it, and that was measured rather than assumed
(see ADR: Deployment strategy). The registrar also refuses nameserver changes, which rules out
Cloudflare's free tier until the domain can transfer, earliest late October
2026. The pattern survived both walls. Only the vendor is temporary.

## What is left of the production design

Open Infrastructure (Bicep) in this menu. Until 20 September 2026
infra/main.bicep described a design nobody ran: App Service behind Azure
Front Door with the origin locked. The App Service half of it runs now, and
the file is the description of what runs: the plan, the two sites, every
setting, the identity and the registry pull. It is deployed in incremental
mode only, because the same resource group holds the databases, the registry
and the identity, and none of them is in a template on purpose (ADR: One
plan, two sites).

What is left is Front Door and the origin lock, behind one parameter that
defaults off. The origins are reachable directly today, as the container
groups were, and what that does and does not expose is on the Security page.
When the subscription allows Front Door it is one parameter, and the domain
layer means the public URL does not change in the switch, as it did not
change in this one.

## Files

- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep) and [`infra/appservice.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/appservice.bicep): what runs, the plan and
  the two sites (served above as Infrastructure (Bicep)).
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml) and [`infra/aci-theyard-cosmos.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard-cosmos.yaml): the two
  container groups that ran until 1.0.0.156, stopped and kept as the way back.
- [`infra/cosmos/`](https://github.com/SteveStout/TheYard/tree/main/infra/cosmos): the container definitions the second store is built from.
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the HTTPS edge.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image both of them run.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): how a merge becomes a roll.
