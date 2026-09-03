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

## Addendum, 2026-09-03: the last sentence stopped being true

The decision above ends by accepting a publicly reachable origin on the grounds
that it is "carrying no secrets and no persistent user data".

That was accurate when it was written on 31 August. It stopped being accurate on
3 September and nobody came back to this record to say so. Two versions changed
it. 1.0.0.48 gave bids owners, which means accounts: ASP.NET Core Identity, real
password hashes, a session token. 1.0.0.49 moved the store to Azure SQL Database,
which means those accounts and every bid outlive the container.

So the origin now fronts persistent user data, and the argument that made an
unlocked origin acceptable no longer holds on its own terms.

What is written down about a system is part of the system. A record that quietly
becomes wrong is worse than one that says plainly when it stopped being right,
because the second kind can be read. This is the second kind.

### What actually stands between the internet and that data today

The origin lock is still not deployed and is still the right answer. Meanwhile
this is what the risk actually consists of, stated so it can be argued with:

- **The database has no password to steal, because it has no login to hold one.**
  The server was created Entra-only, so there is no SQL authentication path at
  all. The container reaches it as the managed identity it already carried, and
  the connection string in its environment is a server name, a database name and
  an authentication mode (ADR: The SQL Server backend).
- **That identity can read and write rows and nothing else.** It holds
  `db_datareader` and `db_datawriter`. It cannot alter a table, which is why the
  schema lives in a SQL project published separately.
- **Passwords are hashed by Identity, not stored**, and the session is a signed
  token in an httpOnly cookie the page cannot read (ADR: Accounts and per-user
  bids).
- **The public surfaces were reviewed for what they publish** on 3 September, and
  that review found and fixed a path by which a database exception message,
  which carries the server name and the caller's address, reached a public page
  (ADR: Reviewing my own work).

### What is still owed

The origin lock. `enableFrontDoor=true` with `siteLock` is in the template and
has never been deployed, because the subscription that would allow it is the
subscription this project deliberately does not pay for. That trade was worth
making when the origin held nothing. It is a smaller trade now, and it should be
revisited before this application holds anybody's data but mine.

Recorded rather than fixed, because pretending an unfunded control is in place is
the failure this addendum exists to correct.

## Files

- [`infra/main.bicep`](https://github.com/SteveStout/TheYard/blob/main/infra/main.bicep): the target as code. `enableFrontDoor` and
  `computeKind` select it; `fdProfile`, `fdEndpoint`, `fdOriginGroup`,
  `fdOrigin` and `fdRoute` are the Front Door; `plan` and `site` are the App
  Service origin; `siteLock` is the origin lock this record is about, shown
  live below.
- [`docs/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/HOSTING.md): why the site does not run this today, and what
  runs instead.
- [`infra/aci-theyard.yaml`](https://github.com/SteveStout/TheYard/blob/main/infra/aci-theyard.yaml): the container group that does run, per
  ADR: Deployment strategy.

The origin lock, read from this build:

```live path=infra/main.bicep region=origin-lock
```
