# Quality you can watch

Every rule this codebase lives by is a test, and fifteen numbers about its shape may only get better. This page shows both, read from the build that serves it, including the two weeks the numbers got worse.

## In plain words

I built this site with AI while most of my attention was elsewhere, and the risk in that is quality slipping while nobody is looking. So the decisions are written down as records, the records that a machine can check are tests, and a scanner counts fifteen things about the code on every version. A version that breaks a rule or makes any of them worse does not ship.

Nothing on this page is typed in. Every number is read, when you open it, from files the build writes: the quality history (one row per shipped version, {{live:quality-versions}} so far), the gate's own test results, the rules table and CodeRabbit's log. The tests that hold this page fail if it says anything the data does not.

What that is worth to a team: quality that is measured on every change and can only ratchet one way, with the evidence public and the slip left in.

## The slip, and the turn

In the two weeks to 29 October the code doubled, every yes-or-no rule held, and everything nobody was counting got worse. Async methods with no way to cancel them went from {{live:quality-first async_without_cancellation}} on 15 October to {{live:quality-peak async_without_cancellation}}. Declarations with no summary peaked at {{live:quality-peak undocumented_declarations}}. On 29 October, at 1.0.4.115, the ratchet went in ([ADR-122](https://theyard.stevenstout.biz/?doc=adr-the-numbers-that-may-only-fall)): fourteen numbers in one file, fifteen since 1.0.5.1, read by the gate on every version, each allowed to move only the good way.

Today the async count is {{live:quality-now async_without_cancellation}} and the undocumented count is {{live:quality-now undocumented_declarations}}. The gold mark on each chart is 1.0.4.115. The dashed start is the pre-scan, counted before the scanner existed; the scanner counts more strictly than the pre-scan did, so where the dashed line steps up into the solid one, part of that step is the two instruments disagreeing rather than the code. Every solid point is the same scanner, today's, read over that version's own tree.

## The numbers over time

One line per number, the whole history, every shipped version from 1.0.4.115 on.

```quality-trend
Async methods with no CancellationToken | may only fall | 1.0.4.115
{{live:quality-trend async_without_cancellation}}
```

```quality-trend
Declarations with no summary | may only fall | 1.0.4.115
{{live:quality-trend undocumented_declarations}}
```

```quality-trend
Clock reads outside a composition root | may only fall | 1.0.4.115
{{live:quality-trend clock_outside_composition}}
```

```quality-trend
Comments carrying a date or a name | may only fall | 1.0.4.115
{{live:quality-trend history_or_name_comments}}
```

```quality-trend
Service classes with six or more dependencies | may only fall | 1.0.4.115
{{live:quality-trend wide_service_classes}}
```

```quality-trend
Files within ten lines of the 300-line cap | may only fall | 1.0.4.115
{{live:quality-trend files_within_10_of_cap}}
```

```quality-trend
Behaviour types no test names | may only fall | 1.0.4.115
{{live:quality-trend behavior_types_never_named_in_a_test}}
```

```quality-trend
React files with no header saying what they do | may only fall | 1.0.4.115
{{live:quality-trend react_files_without_header}}
```

```quality-trend
Test lines per production line | may only rise | 1.0.4.115
{{live:quality-trend test_to_prod_ratio_higher_is_better}}
```

```quality-trend
xUnit facts and data rows | may only rise | 1.0.4.115
{{live:quality-trend dotnet_tests_higher_is_better}}
```

Held at zero on every version since the ratchet went in, so there is no line to draw: project references pointing outward ({{live:quality-now onion_outward_refs}}), inner rings reaching the disk or the network ({{live:quality-now inner_ring_leaks}}), one slice naming another ({{live:quality-now cross_slice_usings}}), blocking waits ({{live:quality-now blocking_waits}}) and files over the cap ({{live:quality-now files_over_300}}).

## Coverage

Line and branch coverage from CI on main for each version, against a floor of 90 per cent of lines and 75 per cent of branches that no change may lower ([ADR: Counting what the tests cover](https://theyard.stevenstout.biz/?doc=adr-coverage)). Today: {{live:coverage-now lines}} per cent of lines, {{live:coverage-now branches}} per cent of branches.

```quality-trend
Line coverage, per cent | floor 90 | 1.0.4.115
{{live:coverage-trend lines}}
```

```quality-trend
Branch coverage, per cent | floor 75 | 1.0.4.115
{{live:coverage-trend branches}}
```

## CodeRabbit, the last reviewer

An AI review bot reads every version's pull request inside the gate. It is at level {{live:coderabbit-level}} of four, where it comments and no finding holds a release; each step up is my decision, on this log ([ADR-129](https://theyard.stevenstout.biz/?doc=adr-coderabbit-one-rung-at-a-time)).

{{live:coderabbit-rows}}

## What is still thin

Named plainly, with the numbers:

- React files with no header comment saying what they do: {{live:quality-now react_files_without_header}} of {{live:quality-thin react_files}}. The site's own folders, src/app and src/library, carry one in every file, and so does every component folder's entry since 1.0.5.1; the components themselves and the rest of src/lib do not yet.
- XML documentation on TheYard.Data, the records every other ring reads: {{live:quality-thin data_xml_docs_pct}} per cent of its declarations.
- Service classes taking six or more dependencies: {{live:quality-now wide_service_classes}}. Each one is a class doing more than one job.

Each is paid down one version at a time, and each payment lowers its limit in the same commit, so it cannot come back.

## The rules, and the tests that hold them

Every rule a machine can check, from [ADR-075](https://theyard.stevenstout.biz/?doc=adr-rules), with the tests that hold it and how they did in the gate this build shipped from.

{{live:quality-rules}}

## The ratchet, read from this build

The limits, one per number:

```live path=.github/quality-ratchet.json region=*
```

And the block of the scanner that measures them:

```live path=.github/quality.py region=ratchet
```

## Files

- [`data/quality-history.json`](https://github.com/SteveStout/TheYard/blob/main/data/quality-history.json): the history, one row per shipped version
- [`scripts/quality_history.py`](https://github.com/SteveStout/TheYard/blob/main/scripts/quality_history.py): rebuilds it from git and adds a row on every ship
- [`api/TheYard.Api/Features/Docs/QualityCounts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/QualityCounts.cs): every live number on this page
- [`api/TheYard.Tests/QualityHistoryTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/QualityHistoryTests.cs): holds the history to the scanner and this page to the data
