# Hosting

TheYard is on the internet at https://theyard.stevenstout.biz. This page is the
parent record for how that works, written for a hiring manager or anyone
learning how a small production setup fits together. The records under it in
this menu are its children: read this page for the shape, open a child for the
full reasoning behind one decision. Everything, the infrastructure code
included, is served from these menus; nothing requires opening the repository.

## Websites and resources used

- **Azure (portal.azure.com).** Runs the app: Container Instances for the
  compute, Container Registry for the image. The only place code executes.
- **Wix (wix.com).** The domain registrar. Holds stevenstout.biz and answers
  DNS; one CNAME record points theyard at the edge.
- **Netlify (netlify.com).** The free edge. Terminates HTTPS, holds the
  certificate, and forwards every request to Azure unchanged.
- **Let's Encrypt (letsencrypt.org).** Issues the certificate at no cost;
  Netlify renews it automatically.
- **Cloudflare (cloudflare.com).** Configured and dormant; becomes the edge
  after the domain transfers registrars, around late October 2026.
- **GitHub (github.com/SteveStout/TheYard).** Holds the code, runs the test
  wall on every push, and feeds the edge deploys.

## The chain, request by request

1. **DNS.** theyard.stevenstout.biz is a CNAME record at the registrar (Wix)
   pointing at the edge. TTLs sit at 30 minutes while the setup is young so
   changes propagate fast. They get lengthened once things are boring.
2. **Edge.** Netlify's free tier terminates HTTPS and forwards every request
   unchanged. The entire edge is three files in this repository, deployed from
   GitHub on every push.
3. **Origin.** Azure Container Instances runs the Docker image in RG-THEYARD-SS
   (westus2), serving HTTP on port 8080. Azure does all the compute. The edge
   only forwards.

## The certificate

Let's Encrypt at the edge, issued and renewed automatically. Nothing was
purchased and nothing expires by surprise. The edge-to-origin hop stays plain
HTTP in phase 1 because the container has no TLS listener; the phase-2 managed
certificate closes that hop end to end.

## Why not Front Door today

The free trial refuses to create it, and that was measured rather than assumed
(see ADR: Deployment strategy). The registrar also refuses nameserver changes, which rules out
Cloudflare's free tier until the domain can transfer, earliest late October
2026. The pattern survived both walls. Only the vendor is temporary.

## How this would be hosted in production

The production design exists as code and costs nothing to keep. Open
Infrastructure (Bicep) in this menu: infra/main.bicep stands up App Service
behind Azure Front Door with the origin locked, so nothing reaches the app
except through the edge. Deploying it is one command and two parameter flips.

It stays undeployed on purpose. A public demo carrying no secrets does not
need a paid stack, and keeping the bill at zero while keeping the design
reviewable is part of the engineering story. If this were a production
workload, that file is exactly what would run, and the domain layer means the
public URL would never change in the switch.

## Files

- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the container group that runs today.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): the production design, deliberately
  undeployed (served above as Infrastructure (Bicep)).
- [`netlify.toml`](https://github.com/SteveStout/TheYard/blob/main/netlify.toml) and [`edge/_redirects`](https://github.com/SteveStout/TheYard/blob/main/edge/_redirects): the HTTPS edge.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the image both of them run.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): how a merge becomes a roll.
