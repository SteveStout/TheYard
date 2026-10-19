# ADR: Unused resources are deleted

Status: accepted, 2026-10-19. Anything in Azure that nothing reads, runs or routes to is deleted once a read proves it, not stopped, paused or kept in case. The rule is Steve's, set the day the three .NET web apps became one: "always delete unused resources". The same day it removed the three old web apps, The Shed's image repository, the rendering service's image tags past the newest ten, and the free database paused since 14 September.

## Reading list

For Steve, when he is fresh, in this order:

1. [ADR: One web app for every .NET process](https://theyard.stevenstout.biz/?doc=adr-one-web-app-for-every-net-process), the addendum: the deletes this rule was written after, with their times.
2. [Hosting](https://theyard.stevenstout.biz/?doc=hosting), Websites and resources used: what is left, and what each is for.
3. Microsoft Learn, [Azure Advisor cost recommendations](https://learn.microsoft.com/en-us/azure/advisor/advisor-cost-recommendations): Azure's own list of the idle resources it looks for.

## In plain words

A resource nobody uses still costs something: money on some, attention on all of them. It sits in every list, every inventory and every security review, and someone has to remember why it is there. Stopping it keeps all of that and saves only the running cost. So when a read shows nothing uses a resource, it goes.

What that is worth: the resource group says what the system is. A reader, a reviewer or the next engineer can list it and trust that every line is in use.

## Context

On 19 October the SQL site, the Cosmos DB site and The Shed moved into one web app. That left three web apps, a registry repository and a paused database that nothing used. The first two web apps were deleted within the hour; the third waited only for The Shed's DNS record to move. The registry still held 43 tags of an image no web app runs and 81 of the rendering service's, of which a roll can only ever go back to the last few. The free database had been paused since the sites moved to the Basic one on 14 September and was kept "in case", at $0.

## Decision

1. **Read before deleting.** A resource is unused when every one of these reads says so: no web app's image, setting or binding names it; no workflow or template names it; no DNS record points at it; nothing has read or written it in the window its own metrics keep. The read is a script that writes what it found, kept beside the lane that ran it.
2. **Guard the delete.** The delete script names the one resource by its full name, refuses anything the live sites read, and checks the state the read found (stopped, paused, no running image) before it acts.
3. **Prove after.** Every public page is read again after the delete, the same checks as a roll, and the result is kept with the delete.
4. **Record it.** The record that made the resource unused gets the delete's time in its addendum, and the Hosting page's resource list loses the row.
5. **What is not unused.** Data a resource holds that something might need again is exported before the resource goes, or the delete waits for the owner's word. A permission no workflow uses is unused too, but a role assignment is a change to an identity, so it waits for the owner and is listed until it goes.

## What was considered

- **Stop or pause, and keep.** The usual habit. It keeps every cost but the running one, and the list stops saying what the system is.
- **Delete on a schedule.** A sweep that deletes whatever looks idle. Fast, and wrong the first time a resource is idle on purpose: the deploy identity, the managed certificate and the registry are all quiet for days.
- **Delete when a read proves it, by hand, at the change that made it unused.** Chosen. The change that orphans a resource is the moment its reason to exist is known.

## Consequences

- The resource group lists only what runs: the plan, the one web app, the rendering service, the Basic database, the Cosmos DB account, the registry, the identity, Application Insights and its workspace, Communication Services and its domain, and The Shed's certificate.
- The registry keeps the tags a roll can go back to: the main image's newest ten, trimmed by Deploy, and the rendering service's newest ten.
- Open: the deploy identity still holds Azure Container Instances Contributor on the group, used by nothing since the container groups retired on 19 October. It goes on the owner's word.

## Addendum, 2026-10-19: what this rule removed the day it was written

| when (UTC) | what |
| --- | --- |
| 15:52 and 15:53 | APP-THEYARD-SS and APP-THEYARD-COSMOS-SS, after 33 checks on the one web app |
| 19:36 | APP-THESHED-SS, once both public resolvers named the one web app for The Shed |
| 19:42 to 19:47 | The registry's `theshed` repository, 43 tags; The Shed ships in the main image. The rendering service's 71 tags past its newest ten; the tag it runs kept |
| 19:51 | `sqldb-theyard-ss`, the free database paused since 14 September, on Steve's word; the Basic database the sites read untouched |

## Where it sits

No code. The scripts that read and delete run from the operator's PC, because the deploy identity holds no right to delete and should not. The record and the Hosting page's resource list are what a reader checks.

## Files

- [`docs/hosting/HOSTING.md`](https://github.com/SteveStout/TheYard/blob/main/docs/hosting/HOSTING.md) - Websites and resources used
- [`docs/decisions/ADR-105-one-web-app-for-every-net-process.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-105-one-web-app-for-every-net-process.md) - the merge that orphaned the resources removed here
