# ADR: The rules a change has to pass

Status: accepted, 2026-09-15, shipped as 1.0.0.136.

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
| A catalogue a site does not serve is let go when nobody has asked for it in a while, never while it is in use or loading, and the one a site serves never | ADR: One plan, two sites | WarmthTests |
| Every record opens with its title, says what became of it, and ends with its Files section | This record | RecordShapeTests |
| Every rule in this table names a test that exists, and every record it cites exists | This record | RuleTableTests |
| Code shown in a document is read from the build at request time, never pasted | ADR: Live code samples | LiveSamplesTests, LiveSampleCoverageTests |
| Every count a living document states is the count | ADR: The public face | PublicFaceTests |
| The slug, the catalog and the sidebar offer the same documents | ADR: The staff review | DocsCatalogTests |
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
| Every hex on the Colour and style page is a token's value, every colour token is on the page, and every contrast figure it states is the figure the tokens give and clears its bar | ADR: The palette | StyleRulesTests |
| A chart series is never a status colour, and a line takes a status tone only for server errors | ADR: The Admin tab, as a product | StyleRulesTests |
| Gold is trim: only the header, the brand mark and the named trim use it, never a chart's line, a tile or a ring | ADR: The palette | StyleRulesTests |
| There is one header gradient, defined once, and every header bar uses it | ADR: The palette | StyleRulesTests |
| Nothing that holds a word or an image is faded, a quiet word is never on the bare ground, and the browser suite and the token test that hold those are still there | ADR: The glass look | StyleRulesTests |

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

## Files

- [`docs/ADR-075-the-rules-a-change-has-to-pass.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-075-the-rules-a-change-has-to-pass.md): this record, which the table below it is read from.
- [`api/TheYard.Tests/RuleTableTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RuleTableTests.cs): the test that keeps the table honest.
- [`api/TheYard.Tests/RecordShapeTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordShapeTests.cs): the shape every record keeps.
- [`api/TheYard.Tests/RecordLinksTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/RecordLinksTests.cs): the links and citations, in both directions.
- [`CLAUDE.md`](https://github.com/SteveStout/TheYard/blob/main/CLAUDE.md): what an agent reads before it changes anything, which now points here.
- [`docs/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): the rules no test holds (served as Coding and Commenting Style under App Architecture).
