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
