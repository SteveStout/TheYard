# ADR: The rules a change has to pass

Status: accepted, 2026-09-15, shipped as 1.0.0.136.

## In plain words

Every rule this project lives by that a machine can check is listed here, next to the test that checks it, and another test checks the list itself. Break a rule and the build fails and names it.

What that is worth: a new developer finds a rule before breaking it, not after, and the organization keeps its standards when people come and go, because the rules live in the build instead of in someone's head.

## Context

Seventy-five records hold the decisions this project has made, and most of the standing ones are already
enforced by a test rather than by memory. Nothing said which test. A reader who wanted to change
something had two ways to find the rule that governed it: read all seventy-five records, or break one and
read the failure that came back. The first is not a thing anybody does, and the second is the reason a
rule gets discovered at the wrong moment.

There is a second problem, and it is the one that prompted this. The record set has a shape: a title the
index can read, a status line saying what became of the decision, and a Files section pointing at the
code it decided about. That shape was held by habit, and habit lasted until the seventy-fourth record,
written thirteen days after the convention settled, which opened with a bold status line in a date format
no other record uses. Nothing failed, because nothing was looking.

## Decision

The rules that a machine can check are listed here beside the test that checks them, and the list itself
is checked. A rule with no test in this table is a rule the next change will break.

## The rules, and what holds each one

| The rule | Where it was decided | What holds it |
| --- | --- | --- |
| Record numbers run from one with no gap, and every record the sidebar offers has a file | ADR: The Decision Records index | RecordLinksTests |
| Every repository link in every document points at something that exists | ADR: Docs and testing | RecordLinksTests |
| Every record a comment cites, by name or by number, is a record that exists | ADR: Docs and testing | RecordLinksTests |
| Every link from a document to this site opens a page: a document as `?doc=` with a slug the catalogue serves, a drawing the catalogue draws, and no bare local address in prose | ADR: Docs and testing | RecordLinksTests |
| Every address this site serves answers with something, and a document is served as markdown | ADR: Every page, checked at every roll | PageStatusTests |
| The sweep checks every document and every drawing the catalogue serves, and its own requests are not counted as traffic | ADR: Every page, checked at every roll | PageStatusTests |
| The container samples its own memory and processor share, and each store's reading is the one that store actually keeps | ADR: What the machines are doing | MachinesTests |
| The request ring folds into minutes, each with its own median and its own errors, and the hour and the month are drawn from the one shape | ADR: The Admin tab, as a product | MachinesTests |
| A public ring hands on what it takes and not what it refuses, a kept entry is the entry the ring serves with the spine's private fields empty, it outlives the widest window that reads it, and the keyed log shows none of them | ADR: Logs that outlive the container | KeptRingTests |
| A kept minute leaves out a figure nobody read, a window is folded into the buckets it is drawn in, and the store's grouped query answers what the folding does | ADR: What the machines are doing | MachineHistoryTests |
| An identity token is asked for at whichever door the host has, and a web app's card claims no restart count it was never given | ADR: One plan, two sites | AzureSelfTests |
| Every setting a container group carried is a setting the two sites carry, and nothing deploys a template in complete mode | ADR: One plan, two sites | AppServiceTemplateTests |
| The living documents that describe what runs name the plan size the template declares | ADR: A rendering service beside the API | AppServiceTemplateTests |
| A request on a store whose catalogue is cold waits for the load without blocking a thread, and a warm store is waited on for no time at all | ADR: One container, both stores | WarmthTests |
| No production comment leans on a version number, a date or a review to stand in for its reason | ADR: Code that reads like code | HouseVoiceTests |
| Every record opens with its title, says what became of it, says where it sits in the rings above its Files section, and has a Files section with at least one link | This record, ADR: Onion and SOLID, how this codebase holds them | RecordShapeTests |
| Every rule in this table names a test that exists, and every record it cites exists | This record | RuleTableTests |
| Dependencies point inward: Data and Domain use nothing outside .NET's base class library, Application uses only Domain and Data, and the document store adapter borrows only the shared user from the relational one | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Endpoints never reach a store or the service container, reach the auction only through Application, the host never queries a database itself, and services are registered only in the composition root | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Domain never reads a file, calls the network or reads the system clock or a random number itself, and Application never reads a file or calls the network | ADR: Onion and SOLID, how this codebase holds them | OnionTests |
| Every package the build uses has a row in the versions record with the version in use, and a row that is behind says why | ADR: Technology versions | TechnologyVersionsTests |
| Code shown in a document is read from the build at request time, never pasted | ADR: Live code samples | LiveSamplesTests, LiveSampleCoverageTests |
| The record count on every living document, and the README's xUnit and Playwright counts, are the counts the build declares | ADR: The public face | PublicFaceTests |
| The sitemap lists every document the catalogue serves and nothing else, and llms.txt links only to what the site serves | ADR: The public face | PublicFaceTests |
| /about is a served page with its own head, its Person names only his name, title, profiles and city, the sitemap lists it with the build's day, and the verification tag is written only when it is given | ADR: Every diagram opens on its own page, ADR: The public face | DiagramPageTests, PublicFaceTests, DockerBuildInputsTests |
| The slug, the catalog and the sidebar offer the same documents | ADR: The staff review | DocumentationCatalogTests |
| Every document sits in the folder of the sidebar section that offers it, the records in `docs/decisions/`, and the root of `docs/` holds only those folders, the changelog and the pictures | ADR: One folder per sidebar section | DocsFolderTests |
| Every file in `src/app` and `src/library`, and every stylesheet under `src`, opens with what it does, what it does not, and which files use it; the last is read against the files that import it, and no file there runs past 300 lines but the ones named with why | ADR: The React configuration, explained for a new developer | FileHeaderTests |
| No production C# file under `api/` and no TypeScript file under `src/` runs past 300 lines; a longer file is split by job and the file that keeps the name lists its parts (tests, migrations and generated files are left out) | ADR: The rules a change has to pass | FileShapeTests |
| The five Style pages are served and are the Style section's rows, say "design token" and never a bare "token" (as do the design token files' comments), carry no em dash, and every live number and live fence on them is one the build can count or read, and every tile and glossary link lands on a real section | ADR: The palette | StyleSectionTests |
| One changelog line per shipped version, newest first, and the deploy reads the version from it | ADR: The changelog, ADR: The version comes from the changelog | ChangelogTests |
| Nothing this repository ships or serves contains an em dash | ADR: Style, enforced | HouseVoiceTests |
| No marker for work that is not happening, no focused test, no console call under `src` | ADR: Broken windows, and the rule that answers them | BrokenWindowsTests |
| Every class is sealed, static, abstract or inherited, and the analyzer that catches the internal half is a warning | Best Practices, Sealed by default | SealedByDefaultTests |
| A pinned package version is a decision, and a build error is not a reason to move it | The incident at 1.0.0.91, in the changelog | PackagePinTests |
| Both deploy workflows answer to one set of constants | ADR: A permanent address for the second site | DeployWorkflowTests |
| The SQL project in source control is the authority for the relational schema | ADR: Data first, and the database in source control | SchemaConformanceTests |
| The server owns the clock and every derived fact, and no request names a day | ADR: Three readers with no memory of the project | AuctionScheduleTests |
| Sold is decided before every other bid rule, for everybody | ADR: Accounts and per-user bids | BidRulesTests |
| Every public endpoint is in the API document with an operation id, a summary, its responses and its lock, and no operator endpoint is | ADR: The API describes itself | ApiDocumentTests |
| No stylesheet or component carries a raw colour; every colour is a token in the one sheet | ADR: The palette | StyleRulesTests |
| Every hex on the Style pages (Colour and style, Background and ribbon) is a token's value, every colour token is on one of them, and every contrast figure they state is the figure the tokens give and clears its bar | ADR: The palette | StyleRulesTests |
| A chart series is never a status colour, and a line takes a status tone only for server errors | ADR: The Admin tab, as a product | StyleRulesTests |
| Gold is trim: only the header, the brand mark and the named trim use it, never a chart's line, a tile or a ring | ADR: The palette | StyleRulesTests |
| There is one header gradient, defined once, and every header bar uses it | ADR: The palette | StyleRulesTests |
| Every page title's underline and every stat tile's top rule is the gold bar, drawn from `--gradient-gold` with `border-image` | ADR: The palette | StyleRulesTests |
| Nothing that holds a word or an image is faded, a quiet word is never on the bare ground, and the browser suite and the token test that hold those are still there | ADR: The glass look | StyleRulesTests |
| One face for the whole site, IBM Plex Sans, with tabular figures set once on the body, and a monospaced face only on code | ADR: The palette | StyleRulesTests |
| Every panel, card and tile is the one shared glass with its rule and brackets, no sheet spaces capitals by a number of its own, and every button is a pill or a circle | ADR: The glass look | StyleRulesTests |
| The Author page holds no address, phone number, email address, age, wedding or engagement, and none of the private names and dates kept off it, which are held as digests so the test does not publish them | ADR: The sidebar | AuthorPageTests |
| Every photograph the Author page serves is a file with no metadata, of the width its `srcset` claims, 1920 wide or with its reason written down, with an alt text, in the one frame; the headed blocks alternate by order; nothing else is in the folder | ADR: The sidebar | AuthorPageTests |
| The Admin tab's charts and gauges are drawn in the Mark VII marks, each 3:1 on white, and every figure the style page states for them is the figure the tokens give; the glass is 30 per cent white and the secondary grey holds 4.5 over it on the ribbons' teal and gold stops (`tokens.test.ts`); no table cell breaks a word (`coverage.spec`) | ADR: The tweaks pass | StyleRulesTests |
| A colour is written once in the token sheet, a token that repeats one is written as it, and a see-through tint is mixed from its token; every width a page asks about is a step on the one scale in `src/lib/breakpoints.ts`; every size, weight, corner, tracking and layer a stylesheet writes comes from the token sheet | ADR: The tweaks pass | StyleRulesTests |
| A shipped test-results file is one only a green gate writes, every suite in it with its counts equal to its rows and nothing failed or skipped, the two passes on Cosmos DB the only ones a gate may carry forward and then only from a named version, and the Admin tab's endpoint serves it or says there is none | ADR: The five-minute gate | TestResultsTests |
| CI runs on every push to main that touches the site, on every pull request and by hand, and a push that touches only the sample skips it | ADR: The five-minute gate | CiTriggerTests |
| The listing and the filter values leave compressed for a caller that asks, and no other address is compressed | ADR: Cache headers | CompressionTests |
| Only the listing and the filter values may be kept by the edge, and never for a request that carries a cookie; the browser is still told no-cache | ADR: Cache headers | CacheHeaderTests |
| The default listing reads its page off the day's schedule order, and that order is the full ending-soonest sort at every instant, ties included | ADR: The search index | ScheduleOrderTests |
| Program.cs is a table of contents under eighty lines that maps no route itself, every route is mapped from a file under `Endpoints/` or `Composition/`, and every class in those two folders is static | ADR: The composition root, split by job | CompositionRootTests |
| Every public class and record in the solution says what it is for in an XML summary, and every positional record names each parameter; only the generated migrations are excused, by name | ADR: The composition root, split by job | XmlSummaryTests |
| The bill on the Admin tab is read from Azure once an hour and never on a request, a resource path is cut to its name and type before anything reaches the wire, the donut names four resources and folds the rest into Others, and a reading that is absent is a sentence and never a zero | ADR: What Azure charges | CostTests |

## How the table is read

`RuleTableTests` takes the last cell of every row, pulls every name ending in `Tests` out of it, and
requires each one to be a class in the test assembly with at least one test in it. Then it takes the
middle cell, pulls every record it names, and requires each one to be a record that exists by that exact
title. A row that cites a record which is later renamed fails here, and so does a row whose test is
deleted or renamed, which is the failure this table is for.

```live path=api/TheYard.Tests/RuleTableTests.cs region=table
```

## The order a change goes in

1. Read the record that governs the thing being changed. This table names it for the rules a machine
   holds; the Decision Records index in the sidebar holds the rest.
2. If the change contradicts a record, the record changes first, as an addendum that says when it
   stopped being true rather than an edit that makes it look like it was always this way.
3. Write the test with the change, in the same commit.
4. The five-minute gate runs green before anything rolls (ADR: The five-minute gate). Any build warning
   is red.

## What this does not claim

Most of what makes this codebase readable is not in the table, because no test finds it. Naming, the
direction a dependency points inside a file, a comment that has outlived its premise, a check that asks
an easier question than the one it was written for: those are held by Coding and Commenting Style and by
reading the work again before it ships (ADR: Reviewing my own work, and what that found). The table is
the floor, not the ceiling.

Nor does it claim the tests are the decisions. A rule is decided in a record, for a reason, and the test
is how the decision survives a busy month.

## Addendum, 2026-10-04: two rows changed

The catalogue keeper is gone (ADR: Kept awake, the addendum of 4 October), so the row that named `WarmthTests` now says what that test holds: a request on a cold store waits for the load without blocking a thread. `HouseVoiceTests` gained a second rule in 1.0.3.71, no production comment leaning on a version, a date or a review, and the table has its row.

## Where it sits

Beside the onion, in TheYard.Tests. RuleTableTests reads this table, and three of its rows are the OnionTests rules that hold the rings themselves.

## Files

- [`docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md): this record, which the table below it is read from.
- [`api/TheYard.Tests/RuleTableTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RuleTableTests.cs): the test that keeps the table honest.
- [`api/TheYard.Tests/RecordShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordShapeTests.cs): the shape every record keeps.
- [`api/TheYard.Tests/RecordLinksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordLinksTests.cs): the links and citations, in both directions.
- [`api/TheYard.Tests/FileHeaderTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs): the header every file in the app shell and the document library opens with.
- [`api/TheYard.Tests/FileShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileShapeTests.cs): the 300-line rule for every production source file.
- [`CLAUDE.md`](https://github.com/SteveStout/TheYard/blob/main/CLAUDE.md): what an agent reads before it changes anything, which now points here.
- [`docs/app-architecture/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/app-architecture/STYLE.md): the rules no test holds (served as Coding and Commenting Style under App Architecture).

## Addendum, 2026-10-03: four more rules

Four rows joined the table with the pass that put the rings under test and the fixes that followed it: three for `OnionTests`, which reads the compiled assemblies and fails the build when a dependency points outward, and one for `TechnologyVersionsTests`, which holds the versions record to the project files. `RecordShapeTests` also requires every record to say where it sits (ADR: Onion and SOLID, how this codebase holds them).

## Addendum, 2026-10-05: one more rule

CI runs on every push to main again (ADR: The five-minute gate, the addendum of 5 October), and `CiTriggerTests` holds the triggers, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.77): compression, and the one address list it reads

The container compresses the catalogue's two reads and nothing else (ADR: Cache headers, the addendum on compression). `CompressionTests` holds both halves, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.79): the edge's copy

The edge may keep the catalogue's two reads for a few seconds (ADR: Cache headers, the addendum on the edge's copy), and `CacheHeaderTests` holds who may be served from that copy and who may not, so the table has its row.

## Addendum, 2026-10-05 (1.0.3.80): the schedule order

The default listing stopped sorting the whole catalogue on every request (ADR: The search index, the addendum on the schedule order), and `ScheduleOrderTests` holds the faster path to the page the full sort gives, so the table has its row.

## Addendum, 2026-10-06 (1.0.3.84): the folders

The documents moved into one folder per sidebar section (ADR: The sidebar, the addendum on the folders), and `DocsFolderTests` holds where each one sits, so the table has its row.

## Addendum, 2026-10-06 (1.0.3.85): a fifth Style page

The Style section gained How the documents are styled (ADR: Live code samples, the addendum on the page that shows them), so the row `StyleSectionTests` holds says five pages, and the same test holds the page's two new live numbers.

## Addendum, 2026-10-06 (1.0.3.86): the folders have their own record

The row `DocsFolderTests` holds now cites ADR: One folder per sidebar section, the record Steve asked for so the folder rule is followed from here on, in place of the addendum to ADR: The sidebar that first described it.

## Addendum, 2026-10-09 (1.0.3.106): the documents and the plan

The plan moved to B2 on 8 October and five pages went on describing B1 (ADR: A rendering service beside the API, the addendum on the documents). `AppServiceTemplateTests` now reads the size from the template and holds every page that describes the machine to it, so the table has its row.
