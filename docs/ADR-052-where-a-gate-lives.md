# ADR: Where a gate lives

Status: accepted, 2026-09-03. A rule this project has enforced since 1.0.0.30
was being checked by a script on one laptop.

## Context

The house rule is that nothing written here contains an em dash. It is a real
rule with a real history: 1.0.0.30 rewrote the last two documents that predated
it, and 1.0.0.35 records em dashes reaching the code comments the site displays
as live samples.

Here is where it was checked, before today:

- `ChangelogTests` asserted the character was absent from `docs/CHANGELOG.md`.
  One file.
- The ship script on the developer's machine scanned the whole repository. Not
  in the repository, not on the runner, not runnable by anybody who clones this.

Everything else in that script has a counterpart in CI. Prettier, oxlint,
`dotnet format`, `tsc`, vitest, the .NET suites, the SQL project build and the
browser suite all run in both places, so the local script is a fast preview of
the same answer. The em dash scan was the exception, and it was the exception
without anybody deciding it should be.

A gate that lives in one person's shipping script has three properties worth
naming, because they are easy to miss while it is passing:

1. It is skipped by any commit that does not go through that script. A fix
   pushed from a phone, a change made on another machine, a merge from a
   contributor: none of them are checked.
2. It cannot fail the build, so a violation ships and is found later by reading.
3. It looks enforced. That is the expensive property. Every decision record, a
   README, a security page and every code comment in the repository were
   covered by a sentence in a document and a loop nobody but one machine runs.

## Decision

The scan is a test.

`HouseVoiceTests` walks the repository from the same root helper the other
file-reading tests use, reads every extension this project writes text in, skips
the vendored and generated trees, and fails naming the file and the line. That
puts it in the .NET suite, which means it runs on the CI runner, in the local
gate, and for anybody who clones this and types `dotnet test`.

Three details are deliberate.

**It reads UTF-8 explicitly.** The PowerShell version did not, at first, and
PowerShell 5.1 reads a file as Windows-1252 unless told otherwise. A reader that
guesses the encoding splits one multi-byte character into two single-byte ones
and then finds whatever it likes in the pieces, which is how a gate can report a
violation in a file whose only crime is a multiplication sign.

**It knows three spellings.** The character, and the two HTML ways of writing
it: a named entity and a numeric character reference. Both of those are plain
ASCII, both render as one em dash in served markdown, and a scan that knew only
the character would pass them. This record cannot quote either one, for the same
reason the test cannot write them as literals: the scan reads this file too.

**It cannot spell what it forbids.** The character is written as a Unicode
escape and the two entities are written in halves that the compiler joins, so
the file that defines the rule does not break it. A checker that fails on its own
source is a confusing way to learn that the check works.

The scan stays in the ship script as well. It costs nothing there and it answers
before a commit rather than after one, which is the correct division: the local
script is a preview, and the suite is the authority.

## Addendum, 2026-09-04: the gate failed on itself

The scan went red on CI, on a commit that changed a caption and a stylesheet,
and green on the same code locally and on the four runs either side of it. That
is the shape of a flake, and it was not one.

The annotation added in 1.0.0.71 named it in one line, publicly, which is the
first time that mechanism has earned itself:

```
Failed TheYard.Tests.HouseVoiceTests.No_file_in_this_repository_contains_an_em_dash
```

Here is the chain. The console logger this job asks for, so that individual
test names appear, prints each test with its parameters spelled out:

```
Passed TheYard.Tests.PublicFaceTests.The_page_head_says_what_this_is(expected: "name=\"description\"")
```

One of this class's own parameters is the em dash it forbids, because a rule
that cannot fail is not a rule and the theory proving it can fail has to contain
one. The job pipes that output through `tee` into a file in the checkout. So the
transcript of the running suite contained the character, sat in the repository
the scan reads, and the scan read it.

Whether it failed depended on whether that line was flushed before the scan
reached it, which is why four runs passed and one did not.

Two things are wrong there and only one of them is about em dashes.

**A build should not write into the source it is building.** The transcripts go
to the runner's temp directory now. That fixes this and every future test that
reads the repository, which is a growing list: the em dash rule, the record
citations, the workflow's own patterns.

**And the scan skips the two transcript names anyway**, because the next person
to add a `tee` will not remember why the first one moved.

Worth being precise about the local gate's part in this: it never saw the
failure and never could, because the ship script writes its transcript outside
the repository already, into `mentor/logs`. The gate that runs in two places was
running against two different trees, and only the runner's had the file.

## Alternatives

**A CI step.** A grep in `ci.yml` would have covered commits and cost less to
write. It would not run for a person who clones this, and it would be a check
whose failure mode is a pattern that matches nothing, which this repository
learned about earlier the same day.

**A linter rule.** Prettier and oxlint see TypeScript and CSS. The rule covers
markdown, C#, YAML, SQL and SVG, so it would need three tools configured
separately to say one thing.

**Leaving it.** The rule had held for forty versions. It had held because one
person shipped everything through one script, which is a fact about the
workflow rather than about the repository.

## Consequences

- The rule is enforced everywhere the code is, and a violation names its file
  and its line rather than a path on somebody's C drive.
- 272 files are read on every test run, which is about a fifth of a second.
- Two extensions are covered that the script's list did not include, `.txt` and
  `.html`.
- One test asserts the scan reads something, because a scan that walks the wrong
  directory passes silently and that is the shape of defect this record exists
  to remove.

## Files

- [`api/TheYard.Tests/HouseVoiceTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/HouseVoiceTests.cs): the scan, what it reads and what it skips.
- [`api/TheYard.Tests/ChangelogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/ChangelogTests.cs): the one-file version this generalises, kept because it also holds the changelog's other rules.
- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the gates that were already in both places.
- [`docs/ADR-042-exemptions-that-hide.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-042-exemptions-that-hide.md): the same subject from the other side, a check that runs and asks an easier question.
