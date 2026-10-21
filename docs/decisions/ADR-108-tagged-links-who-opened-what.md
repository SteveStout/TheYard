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
