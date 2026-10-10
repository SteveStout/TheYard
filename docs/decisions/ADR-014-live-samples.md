# ADR: Live code samples

Status: accepted, 2026-09-02, shipped as 1.0.0.16.

## In plain words

The code shown in these documents is not pasted in. It is read from the running build every time a page opens, with a link to the exact lines on GitHub.

What that is worth: a reader never sees out-of-date code, and the organization's documentation stays true without anyone remembering to update it.

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
([`api/TheYard.Api/Endpoints/DocumentationEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/DocumentationEndpoints.cs),
[`api/TheYard.Api/DocumentationCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocumentationCatalog.cs)):

```live path=api/TheYard.Api/Endpoints/DocumentationEndpoints.cs region=docs-endpoint
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

## Where it sits

Live samples are a host concern and sit in the Api: LiveSamples.cs holds the whitelist and the expander, and DocumentationEndpoints.cs passes every served document through it. LiveSamples has one reason to change (how a fenced block becomes source text), which is single responsibility; the endpoint stays a caller that knows nothing about regions. The price is source code shipped inside the runtime image and a parser on a public endpoint; LiveSamplesTests and LiveSampleCoverageTests hold both to their limits. A private codebase would change the call, since this design publishes its own source to anyone who opens a record.

## Files

- [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs): the whitelist and the expander.
- [`api/TheYard.Api/Endpoints/DocumentationEndpoints.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Endpoints/DocumentationEndpoints.cs) and
  [`api/TheYard.Api/DocumentationCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocumentationCatalog.cs): the one endpoint and the slugs
  it serves through the expander.
- [`api/TheYard.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSamplesTests.cs): the rejection cases and
  the served-record checks.
- [`api/TheYard.Tests/LiveSampleCoverageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSampleCoverageTests.cs): every block in
  the catalogue rendered from the checkout, and every path it names held to
  the Dockerfile (the coverage addendum below).
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

## Addendum, 2026-09-09: coverage, or the note nobody was reading

A live block that cannot be shown renders a one-line note instead of an
error. That was the right decision for the reader, and it had a cost that
took a week to surface: nothing was reading the notes. On the morning of
2026-09-09 a sweep of every served document on both live sites (eighty
slugs, read as the sidebar reads them) found the measuring record showing
"Sample unavailable" where its method should have been. The block named
`scripts/measure_stores.py`, and `scripts/` was not one of the roots, so the
expander declined it exactly as designed, on every machine, from the day the
record was written. Every gate had been green the whole time, because a
sentence is not a failure.

Three things changed. `scripts/` is the seventh root, and the runtime stage
of the Dockerfile copies it, so the block renders on the live site as it
does here. And two tests now read what the reader would have read:
`LiveSampleCoverageTests` expands every served document against the
checkout and fails on any note, and, separately, parses the runtime stage of
the Dockerfile and fails on any live block whose path that stage never
copies, because a glob like `api/TheYard.Api/*.cs` reaches the files beside
it and none in a folder below, which is a second way for a sample to be
present on every developer's machine and "not in this build" on the domain.
That second check is the same class as the second manifest (ADR: The second
manifest), and this is its third instance in this repository: an explicit
list that nothing compared with the thing it described.

The tests are not shown live here, for the reason the whole-file addendum
gives: their source spells out the note and the fence they look for.

## Addendum, 2026-10-04: one folder from the sample beside the app

The onion practices page shows The Shed's architecture tests whole, because they are the example of
the rings drawn as folders in one project. The Shed lives under `samples/maplarge`, which none of the
seven roots reached, so `samples/maplarge/tests/` is the eighth. It is the sample's test folder and
nothing more: the rest of the sample is served by its own site, and the rejection tests gained
`samples/maplarge/Program.cs` to keep it that way. The runtime stage of the Dockerfile copies the one
file the page names, and the coverage test above already fails any live block whose file the image
does not carry.

One consequence is written down rather than fixed. A push that changes only the sample does not
redeploy TheYard, so TheYard's sites show the sample's tests as they were at TheYard's last deploy.
The line under the block names the commit it was read from, so the copy says how old it is.

## Addendum, 2026-10-06 (1.0.3.85): the page that shows them

Steve asked for a page that teaches how the documents are styled, for a developer who wants to copy the idea. It is How the documents are styled, under Style (`docs/style/DOCUMENT-STYLE.md`), and it shows each feature by using it: its own status line drawn as a reading, its sections as panels, a link to one of its own sections, a `swatches` fence, a `readouts` fence and five live blocks, one of them the expander this record decided on. It ends with the five steps to try the pattern in another repository, the last of which is the coverage test this record's earlier addendum added.

The page opens on how many live blocks the library carries and in how many documents, and both numbers are placeholders like the Style guide's. Two measures were added for them, `live-blocks` and `live-documents` (`LiveCounts.cs`): each counts the lines that open a live fence in the documents the catalogue serves, the way the expander finds them. `StyleSectionTests` counts the same fences by hand and holds the two measures to its count.

## Addendum, 2026-10-09 (1.0.3.112): the pages that describe the machine show it

Seventeen of the thirty pages outside the records carried no live block on 9 October, among them the four that describe how the site is hosted and rolled. The rule for filling one is the rule this record set: a live block goes where a sentence talks about that code, and nowhere else, so a page is never padded to clear a count. Under it, Hosting now shows the edge's rules where the chain names them and the rendering service's web app where the design section names it; the Infrastructure overview shows the plan's size as the template declares it, the one line `AppServiceTemplateTests` holds every page to; CI/CD shows Deploy Render's roll and its readiness check, and the edge's ignore rule beside the sentence about the edge deploying itself; and the Web overview shows the font preload plugin its type row describes, with its table of pieces brought up to the build that runs (the script is 305 KB built and the renderer chunk 144 KB; the edge keeps the catalogue's two reads). The pages that still carry no block say why in the story of this work: the changelog and the Author page are exempt, and the rest describe decisions or measurements with no code behind the sentences.

## Addendum, 2026-10-09 (1.0.3.113): the pages that describe the code show it

The same rule, on the five pages that describe the code itself. Data flow shows the overlay its read path's fourth step names, the fetch seam its sixth step names, and the sold rule its write path's second step names. Projects shows the rings as `OnionTests` reads them from the compiled assemblies, where the Tests section names that test. Start here shows the loaders, the one file on its "where a change goes" list a new developer has not met. Security shows the signing key's rule, the lockout, and the check that a session bids only where its account is, each under the paragraph that describes it. Style's one pasted example, the comment above the store write in `PlaceBidAsync`, is now the method itself, read from the build, so the page shows the comment as it stands and the record's own rule about pasted code holds on the page that teaches it.

## Addendum, 2026-10-09 (1.0.3.114): the records that explain a piece of code show it

Twenty-four of the ninety-six records carried no live block. Twelve of them explain one piece of code and name its file, so each now shows that piece from the build, in a region the file already carried, above its Where it sits section: the interceptor (ADR-044), the SQL log port (ADR-043), the Dockerfile's first manifest (ADR-047), the lockout (ADR-050), the registration window (ADR-054), the broken-windows markers (ADR-055), the listing's refresh timer (ADR-056), the document slug (ADR-057), the sitemap held both ways (ADR-053), the versions test (ADR-089), the exception slot (ADR-045) and the page sweep's second look (ADR-077). The other twelve stay as they are, under this record's rule: ADR-032, 046, 048, 049, 052, 058, 065, 082, 084 and 090 decided a method or a place rather than a piece of code, and the code behind them is shown on the record or page that owns it; ADR-041 and 051 would each need a hundred-line block to say what one paragraph already says.
