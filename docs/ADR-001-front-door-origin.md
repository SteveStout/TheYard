# ADR-001: Azure Front Door fronts everything; App Service is the origin

Date: 2026-08-31
Status: Accepted

## Context

TheYard deploys as one container serving both the React SPA and the .NET API.
Requirements set for the deployment: both frontend and backend sit behind a
single global entry point; the origin must never answer the public internet
directly; infrastructure is defined as code; spend stays small and predictable
on a free-trial subscription.

## Decision

Azure Front Door (Standard tier) is the public entry point. The container runs
on Azure App Service (Web App for Containers) as the single origin. The origin
is locked down with App Service access restrictions: allow only traffic from
the AzureFrontDoor.Backend service tag AND require the X-Azure-FDID header to
match this Front Door profile's ID, so requests that bypass the edge are
rejected at the origin.

## Alternatives considered

- Azure Container Apps. Consumption billing and scale-to-zero are its headline
  advantages. Rejected for this design because Front Door health probes poll
  the origin continuously, which keeps the app permanently awake and erases
  the scale-to-zero economics; and because a hard origin lock on Container
  Apps wants Private Link (Front Door Premium, roughly double the base cost)
  or a VNet-internal environment, which is more infrastructure for the same
  lock App Service provides with two settings. Container Apps remains the
  right call for event-driven or multi-service shapes.
- No Front Door, direct ingress. Cheapest, and Container Apps would win that
  shape with managed certificates and scale-to-zero. Rejected by the stated
  requirement for an edge entry point with an unexposed origin.

## Consequences

- Scale-to-zero is off the table; the app runs always-on. Accepted knowingly.
  The cost sits within the trial credit and gets re-decided when the trial
  ends.
- Front Door adds a fixed monthly base fee independent of traffic.
- The upgrade path to a fully private origin is Front Door Premium with
  Private Link; documented here, deliberately not purchased at this size.
- A custom domain and TLS terminate at the edge later without touching the
  origin.