# ADR: Every Claude lane reports its usage

Status: accepted, 2026-10-22. Every lane that runs Claude for this project posts one usage document per run to the site, and the Admin tab's "Claude usage" card draws them. A lane that calls Claude and writes no usage document is a defect. The rule is held for this site's own code by one client that every call goes through, and for the lanes outside it by the keyed route they post to.

## Reading list

1. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab), the lane that posts first and the run log the card joins on.
2. [ADR: One frame for every resource](https://theyard.stevenstout.biz/?doc=adr-one-frame-for-every-resource), the frame the card draws in.

## In plain words

Claude runs in more than one place for this project. The nightly workflow runs it six times a day on GitHub's runners, billed to the plan. Steve runs it on his laptop. Later, the site itself will ask it things. Each of those is a lane, and each lane now sends the site the same small document after a run: which lane, which model, how many tokens went in, came from the cache and went out, how long it took, and what it would have cost at API prices.

The card adds them up: today against the week before, tokens a day per lane, every run in a table, and the nightly runs set beside the Function's slots so a run that reported nothing shows.

What that is worth: the plan's use is a number on a page, per lane, instead of a feeling. The ten runs of 20 and 21 October came to 13.64 dollars API-equivalent in one day of five runs, about 17 a day at six; that is the number the card exists to keep in front of him.

## Context

- **What a run already writes.** The nightly workflow's step "Tokens and wall time" writes `usage.json` beside each run's DONE note from the action's execution file: the wall time, the turns, the four token counts and `total_cost_usd`. The model, the mode and the slot are in the run's folder name and the workflow's outputs.
- **What the cost figure is.** `total_cost_usd` is the price the run would have had on the API. The nightly lane is billed to the plan through `CLAUDE_CODE_OAUTH_TOKEN`, so nothing here is a bill. Every label says "API-equivalent".
- **The database is full.** The shared Cosmos DB database holds twenty-five containers, its limit (ADR: The Nightly Function on the Admin tab, the addendum of 1.0.4.25). A `usage` container cannot be created.

## Decision

1. **One document per run, keyed on the lane and the run.** `UsageRecord` in Domain: lane, kind (`plan` or `api`), model, tokens in, cache write, cache read, tokens out, wall, the API-equivalent cost, the run id, the slot, the mode and when it started. `Validate` answers the one sentence wrong with a document: a lane of 1 to 40 letters, digits, spaces, commas or dashes; a model and a run of plain tokens; counts of zero or more; a slot as HH:MM; a mode of lower-case words; a start inside the last seven days and not in the future. Those rules are also why no folder name, DONE text or file name can travel in one.

```live path=api/TheYard.Domain/Shared/Usage/UsageRecord.cs region=usage-record
```

2. **One route, behind the operator's key.** `POST /api/admin/usage` reads the key from `X-Admin-Key` before it reads the body, answers 401 without it, 400 with the sentence `Validate` gives, 503 with the log's own reason when it cannot keep documents now (a document is never reported kept that was not), and 200 with the key the document is kept under. A document posted again under the same lane and run replaces the first, so a lane may post again after a failure without counting a run twice.

```live path=api/TheYard.Api/Features/Admin/Operations/AdminEndpoints.cs region=usage-endpoint
```

3. **Kept in the plan container.** The documents live in the `plan` container, partitioned on the UTC day, under the kind `usage-run`, each with its own time to live of a year (`CosmosUsageLog`). The card's own minutes live there too, under kinds that open with `claude-`. A process with no document store keeps them in memory and says so.
4. **The nightly lane posts its own.** After "Tokens and wall time", the workflow folds `usage.json` into the document with `jq` (lane "Azure Functions, nightly lane", kind plan, the model and mode from its outputs, the slot from the run's folder time, this run's GitHub id, the run's start) and posts it with the key from the repository secret `THEYARD_ADMIN_KEY` to the site named by the repository variable `THEYARD_SITE`. A missing key, a missing file or a refused post writes one line to the run's summary and never fails the run. A dry run posts with its mode ending `-dry`.

```live path=.github/workflows/nightly.yml region=usage-report
```

5. **The site's own calls go through one client.** `IClaudeClient` in Application is the only way this site's code asks Claude; its adapter, `AnthropicClaudeClient`, calls the Messages endpoint with the key configuration names (`Claude:ApiKey`, never in the repository) and keeps a usage document from every answer's usage block before it hands the answer back, billed as `api`. Nothing calls it yet. `OnionTests.Claude_is_asked_only_through_the_one_client_that_reports_its_usage` fails any other source file of the six projects or the page that names the API's address or an Anthropic SDK, so the first feature that asks Claude cannot skip its document.

```live path=api/TheYard.Application/Shared/Claude/ClaudeClient.cs region=claude-client
```

6. **The card reads the documents, and the lanes come from them.** "Claude usage" sits under Cloud infrastructure after the nightly card and is built through `ResourceCatalog.Cards` like every other. The left dial is today's tokens out against the seven Central days before it, amber at twice the average and red at four times; the right dial is the plan's week, which says "not read: the plan endpoint is not set" until the plan's own usage is read. The chart is tokens a day, one line a lane, in the order the lanes first appear. The four questions: who used what; what each run cost in tokens, as a table with a link to each GitHub run and a warning for a run over 1,200 s of wall; whether the plan is near its limit; and how the runs sit against the Function, slot by slot, joined to the nightly card's run log by GitHub run id, so a run the reader listed with no document reads "no usage reported" and a document whose run the reader never listed reads "run not seen by the nightly reader". Dry runs are listed and left out of every average.

## What was considered

- **The site reading each run's artifacts from GitHub.** It needs an Actions token in the site's settings and a second reader of GitHub; the lanes that run outside GitHub (the laptop) would need a third path. One route that every lane posts to is one writer path and one secret.
- **A container of its own.** The database cannot hold it; see Context.
- **Reading the plan's own bars now.** The endpoint the Claude app reads is not a documented API. It has to be found on the laptop by reading the request the app's usage view makes, and recorded with its read date before a dial depends on it. Until then the dial says it is not read rather than showing a number from anywhere else.

## Consequences

- Nothing posts until `THEYARD_ADMIN_KEY` is set as a repository secret; until then each nightly run's summary says the usage was not reported, and the card says no document has been posted.
- The documents of the runs before this record can be posted once, by hand, from the `usage.json` files beside the runs' DONE notes.
- The card counts what lanes report. A lane that does not post is visible only where the slot join can see it: the nightly lane's runs.

## Where it sits

The record and its rules are in Domain (`UsageRecord`). The port and the memory keep are in Application (`UsageLog.cs`), beside `IClaudeClient`; the route's one handler, `KeepUsageHandler`, is the Usage slice's, in `Application/Features/Usage`. The document store's adapter is in `TheYard.Infrastructure.Cosmos` (`CosmosUsageLog`). The host holds the route (`Features/Admin/Operations/AdminUsageRoutes.cs`), the card's reader and view (`Features/Admin/Cloud/ClaudeSource.cs`, `ClaudeCard.cs`, `ClaudeRows.cs`), the client's adapter (`Shared/Usage/AnthropicClaudeClient.cs`), and the wiring: the log and the client in `Shared/Wiring/UsageRegistration.cs`, because the route and the card are two slices, and the card in `Features/Admin/Cloud`. The page draws the card in the shared frame; it is one slug in `src/lib/workbench.ts`.

## Files

- [`api/TheYard.Domain/Shared/Usage/UsageRecord.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Shared/Usage/UsageRecord.cs) - the document and its rules
- [`api/TheYard.Application/Shared/History/UsageLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/History/UsageLog.cs) - the port and the memory keep
- [`api/TheYard.Application/Shared/Claude/ClaudeClient.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Shared/Claude/ClaudeClient.cs) - the one way the site asks Claude
- [`api/TheYard.Infrastructure.Cosmos/Shared/History/CosmosUsageLog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Infrastructure.Cosmos/Shared/History/CosmosUsageLog.cs) - the documents in the plan container
- [`api/TheYard.Application/Features/Usage/KeepUsageHandler.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Usage/KeepUsageHandler.cs) - the route's handler
- [`api/TheYard.Api/Shared/Wiring/UsageRegistration.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Wiring/UsageRegistration.cs) - the log and the client, registered once
- [`api/TheYard.Api/Features/Admin/Operations/AdminUsageRoutes.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Operations/AdminUsageRoutes.cs) - the keyed route
- [`api/TheYard.Api/Shared/Usage/AnthropicClaudeClient.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Shared/Usage/AnthropicClaudeClient.cs) - the client's adapter
- [`api/TheYard.Api/Features/Admin/Cloud/ClaudeCard.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/ClaudeCard.cs), [`ClaudeRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/ClaudeRows.cs) and [`ClaudeSource.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/ClaudeSource.cs) - the card
- [`.github/workflows/nightly.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/nightly.yml) - the nightly lane's post
- [`api/TheYard.Tests/ClaudeUsageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ClaudeUsageTests.cs) and [`api/TheYard.Tests/ClaudeCardTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ClaudeCardTests.cs) - the rules, the route and the card

## Addendum, 2026-10-22 (1.0.4.30): the card read live, and two faults it found

The card was opened at `/?view=admin&card=claude` on both names, theyard.stevenstout.biz and theyard-cosmos.stevenstout.biz, at 1440 and 390 wide, with Playwright in a real Chromium against 1.0.4.29. All four passes opened the card on the rail, drew both dials, four questions and the readout, answered every API call under 400, and held the page to its width (no horizontal scroll at 390). The card was empty, as it should be: no lane has posted a document yet, because the nightly lane posts only once its key is set, and the four rows said so in words.

The screenshots found two faults that every card under Cloud infrastructure shared, not only this one:

- **Separate facts ran together.** A row's detail is a list of pieces, and the frame drew them with nothing between, so two facts read as one word ("left out of the averagescost is the API-equivalent price", "no usage reportedrun #18"). The frame now puts a middle dot between two pieces unless they already meet as one sentence: the first ends in a space, or the second opens with a space or a stop (`detailJoint` in `src/lib/cloud.ts`, held by `cloud.test.ts`). The cards that built their own " · " keep it; nothing on the server changed.
- **The value broke inside words.** A readout label never shrank, so a long source under it ("the usage documents the lanes post") took the line and left the value a few letters wide ("report / ed"). The label now gives way and wraps, the value keeps up to 55 % of the line, and a value breaks between words.

The card as it reads live at 1.0.4.30, read again with the same Playwright pass at 15:36 Central with both faults gone (separate facts kept apart by a middle dot, the readout's values whole words):

![The Claude usage card on theyard.stevenstout.biz at 1440 wide on 1.0.4.30: both dials not read with their reasons, the four questions, and the readout beside them, before any lane has posted a usage document](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/claude-usage-card.png)
