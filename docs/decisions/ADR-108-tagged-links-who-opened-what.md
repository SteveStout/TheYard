# ADR: Tagged links, who opened what

Status: accepted, 2026-10-20. Every link the operator sends to a recruiter or a hiring manager carries an opaque six-character code, the site counts the code's opens and on which path, and the Admin tab shows the codes. Who a code was sent to is the operator's own list, off the site. The rule is Steve's, from his "$100 bill test": know who opened the links before spending three hours preparing for a call.

## Reading list

For Steve, when he is fresh, in this order:

1. [ADR: Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity), the privacy rule this follows: a row that has no field for a thing cannot leak it.
2. [ADR: The resume has an address](https://theyard.stevenstout.biz/?doc=adr-the-resume-has-an-address), the door a resume link names, which this adds a code beside.
3. [ADR: The onion cut into vertical slices](https://theyard.stevenstout.biz/?doc=adr-the-onion-cut-into-vertical-slices), rule 7: the Links slice is one handler a route over a port, tested with fakes.

## In plain words

A recruiter gets five links: the resume, the site, GitHub, LinkedIn and About Steven. Each one carries `?r=` and a code like `k7f2qa`. When a person opens one, the site counts the code and the path. The operator's list says which code went to whom, so before a call he can read "opened the site twice and the resume once" or "never opened", without the site ever holding a name.

What that is worth: the operator spends his preparation on the people who read, and the site stays as public as it was, because a code names nobody.

## Context

Steve sends links in every reply and every outreach message, and prepares for each call by hand. The Site activity card counts visitors by a daily hash and the resume's door (`?src=email`), which says how many came through which door and never which person. GitHub and LinkedIn count nothing on his side. He asked for the one thing the activity card cannot say: whether the person he is about to talk to opened anything.

The privacy rule stands: no name, address or agent reaches a row, and the Admin tab is public (ADR: The Admin tab, as a product). So the thing that names the person has to live off the site.

## Decision

1. **An opaque code, never a name, in the link.** `?r=` and six lower-case letters or digits (`LinkCodes`). Anything else on `r` is not a code and is not counted, and a code counts only on a path a sent link names: the page's two views, the resume behind its door and the two pass-throughs (`TaggedRequests.IsSent`), so a cache-buster on an API read is never an open.
2. **The site keeps the code, the path and the time, and nothing else.** One document a code a day in the activity container, the third kind beside the hours and the visitors, partitioned on the day with no expiry (`CosmosLinkOpens`). The paths are the resume's door, About Steven, the home page and the two pass-throughs; the busiest twelve are kept. No agent, no address, no referrer.
3. **A preview fetch is not an open.** Email clients, chat tools and mail gateways open a link before the person does. The agent is read for the ones that say what they are (LinkedIn, Slack, Google's proxy, Microsoft's scanners, the usual crawlers) and those are left out (`LinkPreviews`). A scanner that borrows a browser's agent is counted, which errs toward "opened", the cheaper mistake; a browser that says it is headless is a scanner, so the browser spec opens the page with a person's agent.
4. **GitHub and LinkedIn go through the site.** `/api/go/github` and `/api/go/linkedin` count the open and send the visitor on to the address configuration names (`Links:Go`). A key configuration does not name is 404, so the route cannot send anyone anywhere else.
5. **The page reports its own open.** The home page is served by the rendering service, so the code on its address reaches the API only when the page says so: once, on load, `GET /api/links/open?r=&path=/`. The resume and About Steven are the API's own, so their requests count directly. The page reads the code during its first render, because the address-bar mirror rewrites the address in an effect of its own and the code goes with the rewrite; an effect that read the address afterwards reported nothing (the gate of the first 1.0.4.8 found it).
6. **The card shows codes.** "Who opened what" on the Admin tab: code, first open, last open, count, days and paths, for 7, 30 or 90 days. Public like the rest of the tab, and nothing on it names a person.
7. **The operator's list lives in the job-hunt folder.** `make_ref.py` mints a code and writes the row (code, person, company, channel, role, thread, sent) to a spool; `opens_report.py` reads the card's route, joins the list and prints opened or never opened per recipient. Every outbound message's links are tagged; an untagged link is a defect (CONVENTIONS.md).

## What was considered

- **A name in the link** (`?to=jane-doe`). Rejected: a recruiter reads her own name in the address, and personal data in a query string reaches every log between her and the site.
- **Application Insights alone.** It keeps the full request with its query, but it samples, it is read through Log Analytics with a credential, and it holds the user agent beside the query, which is exactly the pairing the activity rule keeps apart. The site's own count is one read and one rule.
- **A new container.** It would need applying to the account by hand; the activity container already has the partition and the retention the count wants.
- **Counting the page's open from the rendering service.** It would put a writer where no store is; one report from the page costs one request.

## Consequences

- A link sent without a code counts as a visitor on the activity card and nothing here; the operator's scripts make the tagged links, so a bare link is the exception.
- An open by a scanner with a browser's agent is counted as a person. The report says "opened", and the call may find otherwise.
- The count lives with the site's activity: no expiry, and the report keeps its own copy on the operator's side every time it runs.
- Until the edge retires, the page at the public name is served through Netlify; the page's own report goes to `/api`, which the edge proxies, so nothing on the edge changes.

## Where it sits

The rule (what a code is, which requests count, how opens fold) and the port are in the Application ring, `Features/Links`, with the three handlers. The adapter is in `TheYard.Infrastructure.Cosmos`, over the activity container; a container with no document store keeps opens in memory. The host's `Features/Links` has the routes and the module; the collector that writes off the request path and the configured pass-through addresses sit at the host's root beside the activity collector, because the request recorder, which is shared, offers to the collector and a shared type reaches no slice (rule 1); the request recorder offers every tagged request. The card is `src/components/admin/LinksCard`; the page's one report is in `App.tsx`. Nothing in Domain knows a link exists.

## Files

- [`api/TheYard.Application/Features/Links/LinkOpens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Links/LinkOpens.cs) - the code, the preview rule, the tagged-request rule, the port, the folding and the memory keeper
- [`api/TheYard.Application/Features/Links/LinkHandlers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Links/LinkHandlers.cs) - the pass-through, the page's report and the card's handlers
- [`api/TheYard.Infrastructure.Cosmos/CosmosLinkOpens.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/CosmosLinkOpens.cs) - opens in the activity container
- [`api/TheYard.Api/Features/Links/`](https://github.com/SteveStout/TheYard/tree/main/api/TheYard.Api/Features/Links) - the routes and the module
- [`api/TheYard.Api/LinkOpenCollector.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LinkOpenCollector.cs) and [`api/TheYard.Api/ConfiguredLinkTargets.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/ConfiguredLinkTargets.cs) - the collector and the configured pass-through addresses
- [`api/TheYard.Api/RequestRecorder.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/RequestRecorder.cs) - the hook that offers a tagged request
- [`src/components/admin/LinksCard/LinksCard.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/admin/LinksCard/LinksCard.tsx) - the card
- [`src/lib/links.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/links.ts) - the page's report and the card's words
- [`api/TheYard.Tests/LinkHandlerTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LinkHandlerTests.cs) and [`api/TheYard.Tests/LinkEndpointStatusTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LinkEndpointStatusTests.cs) - the rules without a host, and the proof through it

## Addendum, 2026-10-21 (1.0.4.13): the record, the live hop and the rule agree

A cold check of 1.0.4.8 on 21 October found the record and the live site apart in five places. From 1.0.4.13:

- **The live hop forwards the code to GitHub, and that stands until the edge retires.** Rule 4 says the route sends the visitor to the configured address; in the process it does, with a bare `Location` (`LinkEndpointStatusTests` asserts `https://github.com/SteveStout/TheYard`). Read from both public names on 21 October, the visitor lands on `github.com/SteveStout/TheYard?r=<code>`: the Netlify edge proxies `/*` to the web app (`netlify.toml`, the proxy rules) and puts the request's query back on the redirect it relays. The origin cannot strip what the edge adds after it answers, so this is accepted and written down: the code, six opaque characters, reaches GitHub's address bar and logs, and names nobody. When the edge retires on 30 October (`Edge:Retires`) and the domain points at Azure, the process's bare `Location` is what the visitor gets and the code stops at the site.
- **The "/go/ needs an edge rule" deferral was wrong.** The edge already proxies every path, so `/go/github` would have reached the API; the routes stay under `/api/go/` by choice, beside the rest of the API.
- **A refused request is never an open.** `TaggedRequests.OpenOf` takes the status the site answered, and 400 and above is not counted: a guess such as `/api/go/nope?r=<code>` answers 404 and adds nothing to the code's paths.
- **The agent rule.** Outlook desktop and Word fetch a link with `Mozilla/4.0 (compatible; ms-office; MSOffice 16)` before anyone reads the message; that is a preview now. "bot" counts only at the end of a word (Googlebot/, Slackbot-), and the bare "Zoom" is gone, so a phone named CUBOT_X30 or Zoomer is a person. A request with no agent at all is a preview, as the code has always said and this record did not.
- **Counts by increment, and a dropped row said.** The document store moved `/opens` by an atomic increment and wrote `/paths` back whole, so two writers in the same second could lose a path count. Every path now moves by its own increment (a path is one JSON pointer step, `/` written `~1`), ten operations a patch; a new path is added only while the row holds fewer than twelve. A row the store refuses is logged by its type and status with a running count, where it was swallowed.
- **Where the collector sits.** The collector and the configured addresses stay at the host's root: a slice's folder holds its Module and static endpoint classes only (`CompositionRootTests`), and these two are instances with state. The shared request recorder now offers through an Application port, `ILinkOpenOffers`, which the collector implements and the Links module registers, so the shared code names the port and not the class; the collector's `Keeper` and counters had no reader and are gone, and a drain that finds opens dropped by a full channel says how many. `MemoryLinkOpens` stays in Application beside the other memory keeps (`MemoryNightlyRunLog`, `MemoryResourceHistory`), the precedent for a container with no document store.
- **The operator's list has eight columns**, not seven: `make_ref.py` writes the lane that minted the code beside the rest, so a report can say which run sent a link.

`LinkHandlerTests` holds the agents, the refused status and the partial updates; `LinkEndpointStatusTests` holds a 404 pass-through leaving the card's row unchanged.

