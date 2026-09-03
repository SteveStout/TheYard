# ADR: Live code samples

Status: accepted, 2026-09-02, shipped as 1.0.0.16.

## Context

Steve's standing rule for this project: every change ships with its
decision record, and the record links to the code and carries live code
samples where possible. The records had the links, and they had samples,
but the samples were copies, pasted in at the commit that shipped them.
Copies rot. Within a day the phone-header record was showing a header block
that no longer existed, because the sidebar had replaced it. A decision
record that shows stale code is worse than one that shows none, since it
reads as the truth.

## Decision

A doc may hold an empty fenced block whose info string names a file and a
region, and the API expands it at request time into the current lines of
that file, read from inside the running image.

- **The block.** Three backticks, the word live, then `path=` and
  `region=`. Nothing else in the doc changes. The served markdown carries an
  ordinary fenced block with a language tag chosen from the extension, then
  one italic line naming the file, the region, and the commit the build
  came from, with a link straight to those lines on GitHub at that commit.
  The word live never reaches the client.
- **The region.** A comment pair in the source, `#region NAME` and
  `#endregion NAME`, in whatever comment syntax the file uses. Names beat
  line numbers because line numbers rot the moment a line is added above
  them. A named end marker lets regions nest.
- **The image carries the source.** The Dockerfile copies `src`,
  `.github/workflows`, and the API's `.cs`, `.csproj` and `.slnx` files
  into the runtime image beside the docs it already carried. Photos and the
  built bundle were already there. The addition measured 279,121 bytes across
  89 files when this was written; the whitelist has widened twice since (the
  addenda below), and the image now carries 367,822 bytes across 119 files,
  uncompressed, before Docker's layer compression.
- **The whitelist is code, checked before any filesystem touch.** A path is
  allowed only when it is plain characters and forward slashes, has no
  empty, dot, or parent segment, and starts under one of the allowed roots
  (`src/`, `api/`, `infra/`, `.github/` when this was written, plus `tests/`
  and `edge/` since) or is one of the named root files listed in the first
  addendum below. That is a string check. Only then does the expander resolve
  the full path, confirm it still sits inside the repo root, and read the
  file.
- **The fallback is a sentence, never a 500.** A path off the roots, a
  missing file, a region that is not there, or a file that cannot be read
  renders one italic line beginning "Sample unavailable" with the reason.
  The rest of the doc serves normally. An unterminated block is left as it
  was written.
- **Every docs endpoint expands.** One helper serves every markdown file
  under `docs/`, so any record can use a live block from now on, and a doc
  with no live blocks passes through untouched.

## In the code

The whitelist, read from this build
([`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs)):

```live path=api/TheYard.Api/LiveSamples.cs region=whitelist
```

The expander, showing itself. This is the most self-referential sample in
the building, and it is shown here with a straight face because it is the
only sample that cannot possibly be stale:

```live path=api/TheYard.Api/LiveSamples.cs region=expander
```

The one endpoint every document goes through, since ADR: The staff review
folded the per-document routes into a catalog
([`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs),
[`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs)):

```live path=api/TheYard.Api/Program.cs region=docs-endpoint
```

The rejection cases the tests hold the whitelist to, read from this build
([`api/TheYard.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSamplesTests.cs)):

```live path=api/TheYard.Tests/LiveSamplesTests.cs region=rejection
```

The copy lines are in the
[`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile),
which the first addendum below added to the whitelist, so other records now
show it live.

## What this replaced

Copied excerpts in ADR: The deploy pipeline, ADR: The phone header, ADR: The
changelog, and ADR: The sidebar. Each now holds live blocks, and each keeps
its prose as the statement of intent while the block shows the current
truth. Where a record described code that no longer exists, the prose says
so and the block shows what stands today.

The alternative considered was fetching samples from GitHub in the browser
at view time. It would have kept the image smaller and tied the sample to
main rather than to the running build, which is the wrong binding: the
site should show the code it is running, not the code someone merged an
hour ago.

## Consequences

- A record can no longer show stale code without saying so; the sample is
  the build's own file, and the link beside it lands on the same lines at
  the same commit.
- The image grew by 279,121 bytes (273 KB) of text. The photo set alone is fifty times that.
- Adding a sample costs two comment lines in the source and one fenced
  block in the doc. Renaming a region without updating the doc renders the
  "Sample unavailable" line, visibly, which is the point.
- The expander is a parsing surface on a public endpoint. It reads only
  whitelisted text files inside the image, writes nothing, and answers with
  a note on every failure, and the tests hold it to that.

## Files

- [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs): the whitelist and the expander.
- [`api/TheYard.Api/Program.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Program.cs) and
  [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): the one endpoint and the slugs
  it serves through the expander.
- [`api/TheYard.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSamplesTests.cs): the rejection cases and
  the served-record checks.
- [`Dockerfile`](https://github.com/SteveStout/TheYard/blob/main/Dockerfile): the sources copied into the image so the container
  can read its own code.
- [`tests/e2e/practices.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/practices.spec.ts): the browser check that a record
  renders its sample and no fallback note.

## Addendum, 2026-09-02: the whitelist widened for the references pass

The original four roots were not enough once every record was to show the
files it decided: the Dockerfile, the edge, the end-to-end specs and the root
configuration files sit outside them. The whitelist now also allows
`tests/`, `edge/` and six named root files (`Dockerfile`, `netlify.toml`,
`playwright.config.ts`, `vite.config.ts`, `package.json`, `index.html`),
the image copies them, and the rejection tests gained cases for the
neighbors that stay out (`.env`, the lock file, anything under `docs/`).
The string check still runs before any filesystem touch.

## Addendum, 2026-09-02: whole files, for the ones that cannot carry a marker

The two records written for a new developer (ADR: Program.cs, explained
and ADR: The React configuration, explained) show package.json, index.html
and the three tsconfig files. package.json is strict JSON and cannot hold a
comment marker, and the others are small enough that a region would be the
whole file anyway. A live block may now say `region=*`, which renders the
whole file with a link to its first line; the star can never collide with
a real region because names are letters, digits, dots and dashes. The three
tsconfig files joined the named root files, `.editorconfig` followed when
the style rules were written down (ADR: App Architecture section), the image
copies them all, and the tests gained the whole-file case. Ten named root
files now, beside the six roots. The test itself is not shown here on
purpose: its source spells out a live fence, and a record that showed it
would fail the check that no fence is left in a served document.

```live path=api/TheYard.Api/LiveSamples.cs region=whole-file
```

## The look, from the live site

![This record open in the app, scrolled to its first sample: the whitelist read from the running build, with the Live from line naming the file, the region and the commit](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-record-live.jpg)
