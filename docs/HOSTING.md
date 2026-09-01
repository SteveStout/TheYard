# Hosting

How TheYard is on the internet, in plain language, with every decision recorded.

**The URL:** https://theyard.stevenstout.biz - permanent, padlocked, no port
number. The certificate is free (Let's Encrypt) and renews itself.

## The chain, request by request

1. **DNS.** theyard.stevenstout.biz is a CNAME record at the registrar (Wix)
   pointing at the edge. TTLs are 30 minutes while the setup is young, so
   future changes propagate fast; they get lengthened once things are boring.
2. **Edge.** Netlify's free tier terminates HTTPS and forwards every request
   unchanged. The entire edge is three files in this repository (netlify.toml
   and the edge/ folder), deployed automatically from GitHub on every push.
3. **Origin.** Azure Container Instances runs the hand-built Docker image in
   RG-THEYARD-SS (westus2), serving HTTP on port 8080. The app, its dataset,
   and the documents behind this menu are all served from that container.
   Azure does all the compute; the edge only forwards.

## Why this shape

ADR-001 calls for an edge in front of an origin that is never exposed directly
(Azure Front Door). Two walls were measured on the way to that target: the
Azure free trial refuses to create Front Door (ADR-004), and Wix, as a
registrar, does not allow nameserver changes at all, which rules out
Cloudflare's free tier until the domain can transfer registrars (earliest late
October 2026, per ICANN's 60-day lock on new registrations; see the ADR-004
addendum). Netlify is the free stand-in playing the Front Door role for
phase 1. The pattern is the same; only the vendor is temporary.

## Certificates

Let's Encrypt at the edge, issued and auto-renewed by Netlify. Nothing was
purchased and no secrets live in this setup. The edge-to-origin hop is plain
HTTP inside phase 1 because the container has no TLS listener; phase 2's
managed certificate closes that hop end to end.

## Phase 2, already designed

Upgrade the subscription off the free trial, deploy App Service plus Front
Door with the origin lock by flipping parameters in infra/main.bicep, and
point the same subdomain at Front Door. The resume URL never changes. That
permanence is the entire point of the domain layer.

## The decision records

- ADR-001: Front Door origin (the target architecture)
- ADR-002: Docker packaging (the image this container runs)
- ADR-003: Azure naming (why everything is RG-THEYARD-SS style)
- ADR-004: Deployment pivots (every measured wall, including the 2026-09-01
  domain addendum: the Wix wall, the Netlify pivot, the dormant Cloudflare
  zone waiting for the registrar transfer)
