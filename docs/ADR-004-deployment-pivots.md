# ADR-004: Deployment strategy: quick push now, true implementation later

Date: 2026-08-31
Status: Accepted

## Context

The author's framing, day one: the plan is a quick push first and the true
implementation second, and Docker was added now precisely so the hosting can
change without the application changing. The container image is the
invariant: the same theyard:v1 runs identically on localhost, Container Apps,
or App Service, so hosting is a subscription decision rather than an
engineering one.

## What the first deployment day measured (free-trial subscription)

1. Azure Front Door: refused outright. Verbatim: "Free Trial and Student
   account is forbidden for Azure Frontdoor resources."
2. App Service compute: quota of zero VMs at every tier tried (B1 and F1) in
   two regions ("SubscriptionIsOverQuotaForSku, Current Limit (Total VMs): 0").
3. Container Apps: the environment provisions, but revision provisioning died
   with "Operation expired" across three environment attempts in two regions
   (eastus2 twice, westus2 once), warm and cold, using Microsoft's own
   hello-world image. Config was ruled out; the subscription class is the
   remaining variable.

## Decision

Phase the delivery and parameterize the phases instead of rewriting:

- Phase 1, quick push: the smallest hosting that accepts the container image,
  currently blocked by the trial subscription itself.
- Phase 2, true implementation: the ADR-001 target, App Service B1 behind
  Front Door with the origin locked to the Front Door ID header.

infra/main.bicep carries computeKind, skuName, enableFrontDoor, and
minReplicas so each phase is a parameter set, not a branch or a rewrite.

## Consequences

The repo, image, tests, CI, and infrastructure-as-code are complete and
portable today; going live is gated on one account decision (upgrade to
pay-as-you-go, or wait). Every wall above was measured, recorded, and
parameterized around the same day it was hit, which is the honest story this
repository tells an interviewer.
## Addendum: phase 1 realized on Container Instances

With Front Door and App Service off the table and Container Apps expiring
revisions, phase 1 ships on Azure Container Instances: one container group,
public FQDN, plain HTTP on port 8080, pulled from ACR with a user-assigned
identity. No TLS and no edge is acceptable for a demo artifact and is exactly
the gap phase 2 closes.

## Addendum, 2026-09-01: the custom domain and the registrar wall

Goal of the day: serve this app at https://theyard.stevenstout.biz with a valid
certificate, changing nothing else about the build.

What we hit. The domain stevenstout.biz was bought at Wix. Wix, as a registrar,
does not allow changing a domain's nameservers at all; their help center says so
and the dashboard's NS section is read-only. That closed the original plan
(Cloudflare's free tier in front, which requires pointing nameservers at
Cloudflare). The ICANN 60-day lock closes the other exit, a registrar transfer,
until about 2026-10-30. Neither wall is the trial's fault; one is Wix product
policy and the other is ICANN policy on new registrations.

What we did instead. Same architecture shape as ADR-001, an edge terminating TLS
in front of the origin, but the phase-1 edge is Netlify's free tier, because it
attaches to a plain CNAME record, which Wix does allow. The entire edge is three
files in this repository: netlify.toml, edge/_redirects (one proxy rule to the
ACI origin on 8080), and edge/README.md. Netlify deploys them from GitHub on
every push. The application never moved; Azure serves every request.

The small choices, so they are not re-litigated later:
- DNS TTLs are 30 minutes (Wix's shortest) during cutover so future changes
  propagate fast; lengthen once the setup is boring.
- A TXT record named subdomain-owner-verification proves domain ownership to
  Netlify for a subdomain of a domain the Netlify account does not own.
- The Netlify project is public on purpose; the platform's private-by-default
  would ask every visitor to log in.
- The Cloudflare zone for stevenstout.biz is fully configured and dormant:
  Flexible SSL, proxied CNAME to the origin, a port-8080 origin rule, and a
  root-and-www redirect. Transferring the domain to Cloudflare once the ICANN
  lock expires activates all of it and retires Netlify with a single DNS change.
- The certificate is Let's Encrypt via Netlify, auto-renewing. Nothing was
  purchased, and no secret lives anywhere in this setup.

Phase 2 is unchanged by any of this: upgrade the subscription, deploy App
Service plus Front Door with the origin lock by flipping the Bicep parameters,
and point the same subdomain at it. The resume URL never changes. That
permanence is the entire point of the domain layer.

## Decision update, later on 2026-09-01

The paid phase-2 deployment is declined, deliberately and indefinitely, for
this demo. The free stack, the ACI origin behind a free TLS edge, is the
permanent hosting for TheYard as a portfolio piece. infra/main.bicep remains
the documented production design, served inside the app under Hosting,
Infrastructure (Bicep), so the target architecture stays reviewable without
running a bill. The same day, the bare domain and www were pointed at the edge
and 301-forwarded to the primary URL, so a trimmed or retyped address still
lands on the app. The resume distributes theyard.stevenstout.biz; the forward
is the safety net.

## Completion note, later still on 2026-09-01

The bare-domain forward finished the same afternoon. One Let's Encrypt
certificate now covers stevenstout.biz, www.stevenstout.biz, and
theyard.stevenstout.biz, and every variant of the bare and www names
redirects to the app with a valid padlock.

The delay taught one operational lesson worth keeping: certificate issuance
validates every attached name, and resolvers that cached the old DNS records
keep serving them until their TTL runs out. A renewal attempted inside that
cache window fails and tells you nothing. Wait out the longest old TTL,
renew once, and it works on the first try.

## Files

- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): what runs, the container group template
  every deploy renders; the container itself is shown live below.
- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): what would run after an upgrade, the App
  Service and Front Door target of ADR: Front Door origin, deliberately
  undeployed.
- [`.github/workflows/deploy.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/deploy.yml): the roll, `az container create`
  from the rendered template (ADR: The deploy pipeline).
- [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md): the chain a request follows today.

```live path=infra/aci-theyard.yaml region=container
```
