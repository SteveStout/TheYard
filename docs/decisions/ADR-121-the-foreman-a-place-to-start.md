# ADR: The Foreman, a place to start

Status: accepted, 2026-10-25, at 1.0.4.82, for rung 0 only. The Foreman reads every card under Cloud infrastructure and the failures Application Insights kept, and writes one page in five sections, with no model and no action: what happened, the readings with their sources, the likely cause or what was ruled out, where to start, and what it could not read. It answers at `GET /api/admin/foreman` and as markdown at `GET /api/admin/foreman.md`, and is the second card under What broke? on the Admin tab. Rung 1, the draft pull request, and the model behind it are not built in this version; Where it sits says why.

## Reading list

1. Microsoft Learn, [Microsoft Agent Framework, frequently asked questions](https://learn.microsoft.com/agent-framework/support/faq): an open-source SDK for agents that reason, use tools and work with other agents, in .NET and Python. Read 2026-10-25.
2. NuGet, [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI): version 1.26.0, last updated 2026-10-23, no prerelease suffix, with Microsoft.Agents.AI.Foundry and Microsoft.Agents.AI.OpenAI beside it. Read 2026-10-25 13:0x CDT; the version is the one to start the model rung from, read again on the day it is built.
3. [ADR: A red that lasts an hour gets a postmortem](https://theyard.stevenstout.biz/?doc=adr-a-red-that-lasts-an-hour-gets-a-postmortem): the incidents, whose timelines the Foreman is to draft from the cards' minutes.
4. [ADR: One frame for every resource](https://theyard.stevenstout.biz/?doc=adr-one-frame-for-every-resource): the cards it reads, each with its tone, its figure and its note.
5. [ADR: The modular monolith](https://theyard.stevenstout.biz/?doc=adr-the-modular-monolith): why the Foreman is a module of the site, not a process of its own.

## In plain words

When something on the site looks wrong, the slow part is knowing where to look. The Foreman is a page that does that first step: it reads every card on the Admin tab, says what moved and when, names what changed nearest to it, and points at the file, the decision record and the test to open first. It also says what it could not read, so a gap is never mistaken for good news. It does not fix anything and it does not guess: today it is arithmetic over the readings, and a model comes later, behind the same page.

## Context

Steve asked on 20 October for "an AI agent monitoring app health ... so the developer has a place to start", run daily, with a written page, his feedback behind the sign-in, and nothing done without him (J:\mentor\PROMPT-THEYARD-FOREMAN.md, his words at 08:50, 08:52, 09:13 and 09:14). On 25 October he approved an addendum: the in-app model adapter is built on Microsoft Agent Framework, with the hosting, evaluation and monitoring each visible. The prompt names this record ADR-112, which was accepted on 22 October for Claude's usage; incidents took ADR-120 at 1.0.4.78, so this is ADR-121.

The prompt draws two halves: an Azure Function on a timer with its own identity, roles and Cosmos DB container, and a GitHub workflow that reasons over the night's document with a model and pushes a page under docs/foreman/ through a ruleset for a GitHub App. Read before building: the site's shared Cosmos DB database holds twenty-five containers, its limit, as ADR: Every Claude lane reports its usage records, so a foreman container would need throughput of its own, a new monthly charge; a second Functions app is a second deployable, which ADR-119 allows only by a change to it first; and every reading the timer would make is one the site already makes each minute, with the identity and roles it already holds.

## Decision

1. **Rung 0 is a module of the site.** `Features/Admin/Foreman` in Application and in the host: a query, its handler and one reading port, `IForemanReadings`, answered by the Cloud infrastructure cards' own reader and the incidents card's failures, so the page and the tab cannot disagree.
2. **The page keeps five sections, in order.** What happened, one sentence with the window; the readings, every card that is not fine with its tone, figure, note and route; the likely cause; where to start, the record, file and test for each card that moved; what it could not read.
3. **No cause without three agreeing sources.** With no model the page has at most two (the nearest start of the process and the newest failure), so it names them as nearest, says what it ruled out, and leaves the cause to the reader.
4. **No action.** Nothing on the page writes, rolls, sets or opens anything.
5. **The model rung is Microsoft Agent Framework over a Foundry deployment**, keyless with a managed identity, when it is built: the package version read that day, not the one above.

## Consequences

- The page costs nothing to run: no new resource, identity, container or schedule.
- A card added to Cloud infrastructure is read by the Foreman once its key is in `ForemanReadings.Read`, and its start row once it is in `ForemanPages.Starts`; `ForemanTests` fails when a start names a record, file or test that is not there.
- There is no nightly copy in git yet: the page is live, not kept.

## Not done in this version, and why

- **Rung 1, the draft pull request.** A workflow can open one with the repository's own token, but a pull request opened that way starts no workflow, so its CI would not run, and the repository setting that lets Actions open pull requests could not be read from this session. The nightly copy and the draft pull request are the next rung, with that setting read first.
- **The model, its evaluation and its monitoring.** A Foundry deployment is a new pay-per-use resource; it is priced, alerted and declared in Bicep in the rung that makes it, with the golden set and the traces.
- **The investigation, the feedback controls and the golden set.** They follow the model.

## What holds it

`ForemanTests`: the five sections in order, every reading with its route, rung 0's cause sentence, what it could not read, every start row's record, file and test present, and the handler through its one port; `ForemanRouteTests`: both routes answer, off Azure saying what they could not read.

## Where it sits

The Admin module in Application and the host, beside the cards it reads; Domain is untouched. Single responsibility: one port, one handler, one page. It cost two routes, a card and a test class, and no resource. What would change it: a reading the site cannot make with its own identity, which would be the first reason for the Function half the prompt draws, written here first as an addendum.

## Files

- [`api/TheYard.Application/Features/Admin/Foreman/ReadForemanHandler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Admin/Foreman/ReadForemanHandler.cs) - the page, its port and its five sections
- [`api/TheYard.Api/Features/Admin/Foreman/ForemanReadings.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Foreman/ForemanReadings.cs) - what it reads
- [`api/TheYard.Api/Features/Admin/Foreman/Module.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Foreman/Module.cs) - the two routes
- [`api/TheYard.Tests/ForemanTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ForemanTests.cs) - the shape, held
