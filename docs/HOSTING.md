# Hosting

TheYard is on the internet at https://theyard.stevenstout.biz. This page is the
parent record for how that works. The four decision records under it in this
menu are its children: read this page for the shape, open a child for the full
reasoning behind one decision.

## The children, in reading order

1. **ADR: Front Door origin.** The target architecture. An edge stands in front
   of an origin that is never exposed directly.
2. **ADR: Docker packaging.** The image this container runs, built by hand and
   reviewed line by line.
3. **ADR: Azure naming.** Why everything is named in the RG-THEYARD-SS style.
4. **ADR: Deployment strategy.** Every wall the free trial put up, measured
   rather than assumed, and the pivots that got the app live anyway. Its
   2026-09-01 addendum carries the domain story: the Wix wall, the Netlify
   pivot, and the dormant Cloudflare zone waiting on a registrar transfer.

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
(child 4). The registrar also refuses nameserver changes, which rules out
Cloudflare's free tier until the domain can transfer, earliest late October
2026. The pattern survived both walls. Only the vendor is temporary.

## Phase 2

Upgrade the subscription, flip the parameters in infra/main.bicep, and point
the same subdomain at Front Door. The resume URL never changes. That permanence
is the entire point of the domain layer.
