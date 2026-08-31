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
## Addendum, 2026-08-31 deploy day

Measured during the first deployment: Azure rejects Front Door creation on
free-trial subscriptions ("Free Trial and Student account is forbidden for
Azure Frontdoor resources"), and Central US had no B1 capacity at that moment.
Interim state shipped instead: enableFrontDoor=false, the app public at its
azurewebsites.net address in another region. The decision above stands; it
activates by upgrading the subscription to pay-as-you-go and redeploying with
enableFrontDoor=true, which also applies the origin lock. Until then the
origin is deliberately, temporarily public.

## Second addendum, same day

Next measurement: the trial subscription's compute quota for Basic-tier App
Service is zero (SubscriptionIsOverQuotaForSku, "Current Limit (Total VMs):
0"), so B1 cannot deploy either. The interim deployment therefore runs on the
F1 Free tier: cold starts and a daily CPU cap, accepted for a demo link.

The rationale behind these trial restrictions, as we read it: Azure Front Door
provisions standing capacity across Microsoft's global edge network, so its
base fee reflects reserved physical infrastructure in many locations at once,
and Microsoft limits that class of service to verified, paying subscriptions
rather than spending-capped trial accounts that are often abandoned. The same
logic shows up as zero trial quota on paid compute tiers.

One upgrade to pay-as-you-go lifts all of it: skuName returns to B1 and
enableFrontDoor flips to true, which also applies the origin lock. The
template already carries both parameters.

## Decision under trial constraints, 2026-08-31 (the author's call)

Proceed WITHOUT Front Door or any trial-restricted resource for now, and say
so plainly here: Front Door with a locked origin remains the best practice
for production and is exactly what this template deploys after a subscription
upgrade (enableFrontDoor=true, computeKind=appservice, skuName=B1). Until
then the app runs on the least-restricted compute the subscription accepts,
publicly reachable by design, carrying no secrets and no persistent user data.
