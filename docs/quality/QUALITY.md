# Quality you can watch

AI made me fast. For two weeks in October it also let the code slip where no test was looking, so I built a ratchet. It measures fifteen numbers about the code on every version, and the gate refuses any version that makes one of them worse. This page is the proof, slip included.

```quality-readouts The ratchet, right now
Numbers held | {{live:quality-held}} | of {{live:quality-count}} | at or under their limit today
Versions measured | {{live:quality-versions}} | | {{live:quality-span}}
.NET tests | {{live:quality-now dotnet_tests_higher_is_better}} | | {{live:quality-join dotnet_tests_higher_is_better}} when the ratchet went in
Rules with a test | {{live:rule-count}} | | checked on every release
```

## The slip, and the ratchet {#the-slip-and-the-turn}

```quality-trend
Async methods with no CancellationToken | may only fall | 1.0.4.115 | slip
{{live:quality-trend async_without_cancellation}}
```

From {{live:quality-first async_without_cancellation}} on 15 October to a worst of {{live:quality-peak async_without_cancellation}}, and {{live:quality-now async_without_cancellation}} today. The gold mark is the version the ratchet went in.

In the two weeks to 29 October the code grew faster than it ever had, every yes-or-no rule held, and everything nobody was counting got worse. Async methods with no way to cancel them went from {{live:quality-first async_without_cancellation}} on 15 October to {{live:quality-peak async_without_cancellation}}. Declarations with no summary peaked at {{live:quality-peak undocumented_declarations}}. On 29 October, at 1.0.4.115, the ratchet went in ([ADR-122](https://theyard.stevenstout.biz/?doc=adr-the-numbers-that-may-only-fall)): fourteen numbers in one file, fifteen since 1.0.5.1, read by the gate on every version, each allowed to move only the good way.

Today the async count is {{live:quality-now async_without_cancellation}} and the undocumented count is {{live:quality-now undocumented_declarations}}. The gold mark on each chart is the version that number joined the ratchet. Every point is the same scanner, today's, read over that version's own tree: before the ratchet, the last version of each day from 15 October; from it on, every version.

## All fifteen numbers, today

```quality-tiles
{{live:quality-held}} of {{live:quality-count}} at or under their limit on {{live:quality-version}}
Async methods with no CancellationToken | {{live:quality-tile async_without_cancellation}}
Comments carrying a date or a name | {{live:quality-tile history_or_name_comments}}
Behaviour types no test names | {{live:quality-tile behavior_types_never_named_in_a_test}}
Declarations with no summary | {{live:quality-tile undocumented_declarations}}
React files with no header saying what they do | {{live:quality-tile react_files_without_header}}
Clock reads outside a composition root | {{live:quality-tile clock_outside_composition}}
Service classes with six or more dependencies | {{live:quality-tile wide_service_classes}}
Files within ten lines of the 300-line cap | {{live:quality-tile files_within_10_of_cap}}
xUnit facts and data rows | {{live:quality-tile dotnet_tests_higher_is_better}}
Test lines per production line | {{live:quality-tile test_to_prod_ratio_higher_is_better}}
One slice naming another | {{live:quality-tile cross_slice_usings}}
Project references pointing outward | {{live:quality-tile onion_outward_refs}}
Inner rings reaching the disk or the network | {{live:quality-tile inner_ring_leaks}}
Blocking waits | {{live:quality-tile blocking_waits}}
Files over the cap | {{live:quality-tile files_over_300}}
```

Each limit moves only one way. When a version improves a number, the limit follows it, so the gain can't be given back.

## The numbers over time

One line for each of the other numbers, the whole history. The async line is at the top of this page.

<div class="quality-grid">

```quality-trend
Declarations with no summary | may only fall | 1.0.4.115 | small | A public type or method with no summary is one the next developer has to read the body of to use.
{{live:quality-trend undocumented_declarations}}
```

```quality-trend
Clock reads outside a composition root | may only fall | 1.0.4.115 | small | Code that reads the clock itself cannot be tested at a chosen time, so its bugs show up at midnight, not in the build.
{{live:quality-trend clock_outside_composition}}
```

```quality-trend
Comments carrying a date or a name | may only fall | 1.0.4.115 | small | A date or a name in a comment is history the commit log already keeps, and it goes stale the day it is written.
{{live:quality-trend history_or_name_comments}}
```

```quality-trend
Service classes with six or more dependencies | may only fall | 1.0.4.115 | small | A class that needs six or more collaborators is doing more than one job, and a change to any of them can break it.
{{live:quality-trend wide_service_classes}}
```

```quality-trend
Files within ten lines of the 300-line cap | may only fall | 1.0.4.115 | small | A file close to the line cap is the next one to be split in a hurry; counting them keeps the split planned.
{{live:quality-trend files_within_10_of_cap}}
```

```quality-trend
Behaviour types no test names | may only fall | 1.0.4.115 | small | A type that does something and that no test names is behaviour nobody has pinned down.
{{live:quality-trend behavior_types_never_named_in_a_test}}
```

```quality-trend
React files with no header saying what they do | may only fall | 1.0.5.1 | small | A page file with no header saying what it does costs every reader a scroll through it first.
{{live:quality-trend react_files_without_header}}
```

```quality-trend
Test lines per production line | may only rise | 1.0.4.115 | small | If the code grows faster than its tests, more of it runs unchecked on every release.
{{live:quality-trend test_to_prod_ratio_higher_is_better}}
```

```quality-trend
xUnit facts and data rows | may only rise | 1.0.4.115 | small | A test that disappears takes its rule with it, so the count may never go down.
{{live:quality-trend dotnet_tests_higher_is_better}}
```

</div>

Held at zero on every version since the ratchet went in, so there is no line to draw: project references pointing outward ({{live:quality-now onion_outward_refs}}), inner rings reaching the disk or the network ({{live:quality-now inner_ring_leaks}}), one slice naming another ({{live:quality-now cross_slice_usings}}), blocking waits ({{live:quality-now blocking_waits}}) and files over the cap ({{live:quality-now files_over_300}}).

## Coverage

Line and branch coverage from CI on main for each version, against a floor of 90 per cent of lines and 75 per cent of branches that no change may lower ([ADR: Counting what the tests cover](https://theyard.stevenstout.biz/?doc=adr-coverage)). Today: {{live:coverage-now lines}} per cent of lines, {{live:coverage-now branches}} per cent of branches.

<div class="quality-pair">

```quality-trend
Line coverage, per cent | floor 90 | | small
{{live:coverage-trend lines}}
```

```quality-trend
Branch coverage, per cent | floor 75 | | small
{{live:coverage-trend branches}}
```

</div>

## Every rule in the rulebook has a test that enforces it

{{live:rule-count}} rules, each written down with the reason behind it and checked by a test on every release. If a change breaks one, it doesn't ship.

```quality-groups
{{live:rule-groups}}
```

Rules the gate does not check are held by suites this file does not record test by test, the job hunt Function's and the machine lanes'.

<details>
<summary>The full rulebook: {{live:rule-count}} rules, the decision behind each, and the test that holds it (for engineers)</summary>

Every rule a machine can check, from [ADR-075](https://theyard.stevenstout.biz/?doc=adr-rules), with the tests that hold it and how they did in the gate this build shipped from.

{{live:quality-rules}}

</details>

## CodeRabbit, the last reviewer

An AI review bot is asked to read every version's pull request inside the gate, and a version it did not get to is logged as not reviewed. It is at level {{live:coderabbit-level}} of four, where it comments and only a Critical finding holds a release; each step up is my decision, on this log ([ADR-129](https://theyard.stevenstout.biz/?doc=adr-coderabbit-one-rung-at-a-time)). Fixed and Wrong (answered as wrong) are read from each request's threads and the versions after it, a finding in neither is still open, an empty cell is one not read yet, and Minutes are the review's own.

```quality-readouts
Versions reviewed | {{live:coderabbit-reviewed}} | of {{live:coderabbit-logged}} | since {{live:coderabbit-first}}
Findings | {{live:coderabbit-findings}} | | across those reviews
Fixed | {{live:coderabbit-fixed}} | | in that version or the next, {{live:coderabbit-unread}} not read yet
Answered as wrong | {{live:coderabbit-wrong}} | | with the reason on the thread
```

<details>
<summary>Every version's review ({{live:coderabbit-logged}} rows)</summary>

{{live:coderabbit-rows}}

</details>

## What is still thin

Named plainly, with the numbers:

- React files with no header comment saying what they do: {{live:quality-now react_files_without_header}} of {{live:quality-thin react_files}}. The site's own folders, src/app and src/library, carry one in every file, and so does every component folder's entry since 1.0.5.1; the components themselves and the rest of src/lib do not yet.
- XML documentation on TheYard.Data, the records every other ring reads: {{live:quality-thin data_xml_docs_pct}} per cent of its declarations.
- Service classes taking six or more dependencies: {{live:quality-now wide_service_classes}}. Each one is a class doing more than one job.

The headers and the wide classes are ratchet numbers: each is paid down one version at a time, the payment lowers its limit in the same commit, and it cannot come back. The XML documentation figure is not on the ratchet yet; it is shown so it is not hidden.

## In plain words

I built this site with AI while most of my attention was elsewhere, and the risk in that is quality slipping while nobody is looking. So the decisions are written down as records, the records that a machine can check are tests, and a scanner counts fifteen things about the code on every version. A version that breaks a rule or makes any of them worse does not ship.

Every count and measurement on this page is read, when you open it, from files the build writes: the quality history ({{live:quality-versions}} versions measured so far), the gate's own test results, the rules table and CodeRabbit's log. A test fails if any of them cannot be read from the data, or if the prose types a count of its own.

**What that is worth to a team:** quality that is measured on every change and can only ratchet one way, with the evidence public and the slip left in.

## The ratchet, read from this build

<details>
<summary>.github/quality-ratchet.json and the scanner block</summary>

The limits, one per number:

```live path=.github/quality-ratchet.json region=*
```

And the block of the scanner that measures them:

```live path=.github/quality.py region=ratchet
```

</details>

## Files

- [`data/quality-history.json`](https://github.com/SteveStout/TheYard/blob/main/data/quality-history.json): the history, one row per shipped version
- [`scripts/quality_history.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/quality_history.py): rebuilds it from git and adds a row on every ship
- [`api/TheYard.Api/Features/Docs/QualityCounts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/QualityCounts.cs): every live number on this page
- [`api/TheYard.Api/Features/Docs/QualityReadouts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/QualityReadouts.cs): the readouts and the fifteen tiles, each number from its own join
- [`api/TheYard.Tests/QualityHistoryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/QualityHistoryTests.cs): holds the history to the scanner and this page to the data
