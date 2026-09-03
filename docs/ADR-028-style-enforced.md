# ADR: Style, enforced

Status: accepted, 2026-09-03, shipped as 1.0.0.39. Steve's ask: "turn implicit
conventions into enforced standards so every later change has a rule to build
against."

## Context

The written half of this already existed. ADR: App Architecture section shipped
`docs/STYLE.md` (naming, layering, the derive-don't-store principle, the comment
rule) and `docs/ARCHITECTURE.md` (the onion and its dependency direction, the
wire contract, the single source of truth), with an `.editorconfig` carrying the
mechanical rules a tool can check.

None of it was load-bearing. `.editorconfig` is a request to an editor, not a
gate, and nothing in CI had an opinion about formatting at all. A rule nobody
can fail is a preference.

## What the measurement said before anything changed

Three checks were run against the tree as it stood, because adopting a tool that
turns out to disagree with the whole codebase is a decision worth making with
the number in hand.

**`dotnet format --verify-no-changes`: failed.** Not on sloppiness. It wanted
three things, and all three were right: whitespace inside a handful of object
initialisers, `using` directives sorted (`TheBlock.Data` before
`TheBlock.Domain`), one unused `using` removed from `Ports.cs`, and the UTF-8
BOM taken off `Program.cs`. That last one is worth naming: `.editorconfig` says
`charset = utf-8`, not `utf-8-bom`, so the BOM was the file disagreeing with the
project's own stated rule. The formatter found a real inconsistency that a
human review had walked past for a week.

**Prettier: 29 files.** Expected, for a codebase that has never had a formatter.

**ESLint: refused to run.** Not a stale peer range. typescript-eslint has an
explicit runtime guard, and this repository is on TypeScript 7:

```
Error: typescript-eslint does not support TS 7.0.
See https://github.com/typescript-eslint/typescript-eslint/issues/10940
```

## Decision

**Four checks, in their own CI job, that fail the build.**

| Check | Owns |
| --- | --- |
| `dotnet format --verify-no-changes` | C# whitespace, using order, unused usings |
| `prettier --check` | TypeScript, TSX, CSS |
| `oxlint --deny-warnings` | the rules a formatter cannot see |
| `tsc -b` | types, and unused locals and parameters |

The style job runs first and alone because it is the cheapest thing here. A
missing semicolon should not cost a browser suite's runtime to discover.

**oxlint instead of ESLint, for as long as that is true.** ESLint is not
available on this TypeScript, and the two ways to pretend otherwise are both
worse than picking something else: downgrading TypeScript to suit a linter
inverts which one is the tool, and forcing the install past its own guard ships
a linter that may silently mis-parse. oxlint carries its own TypeScript parser,
so the TypeScript version is not its business. It ran 106 rules over 48 files in
68 milliseconds.

The `eslint.config.js` that was written first is not in the repository, because
a config for a tool that cannot run is a trap for the next person. The upstream
issue is linked above; when it closes, this is a swap of one dev dependency.

**The linter had to prove it catches things.** A tool that runs clean on a clean
codebase and a tool that does nothing are indistinguishable from their exit
code. Both were checked against a file with four deliberate violations, and the
CI checks were each run once against deliberately bad input:

| Check | clean tree | bad input |
| --- | --- | --- |
| `prettier --check` | 0 | 1 |
| `oxlint --deny-warnings` | 0 | 1 |
| `dotnet format --verify-no-changes` | 0 | 2 |

**The typecheck already earned its keep.** `tsconfig.app.json` has `strict`,
`noUnusedLocals` and `noUnusedParameters`, and `tsc -b` was already in CI. The
linter is not configured to repeat any of that.

## What the linter found on its first real run

Three findings, and one of them is the reason to have a linter at all.

Two were the diagram generator writing to the console, which is what a build
script is for; the config exempts `docs/` and `scripts/` the way it exempts
tests.

The third was `react(set-state-in-effect)` in the component that serves these
documents. `DocDialog` held its load failure as a boolean and reset it inside
the effect that opens the dialog, which is a synchronous `setState` in an effect
and a second render for no reason. The fix was to hold the nonce that failed
rather than a flag saying something did, which makes the flag derivable and
deletes the reset:

```live path=src/components/DocsMenu.tsx region=derived-error
```

That is `docs/STYLE.md`'s own derive-do-not-store rule, violated in the
component that serves `docs/STYLE.md`, found by a tool on its first run. It is
the cleanest possible argument for the tool.

## The diagram, and the runtime that is not shipping with it

`docs/ARCHITECTURE.md` now carries the topology as Mermaid. The point is that
it is text: it lives in the file, changes in the same commit as the thing it
describes, and appears in a diff when the topology moves. GitHub renders it.

Rendering it in the app was tried and backed out on the number. The dynamic
import worked exactly as intended, and the entry chunk went from 289.9 KB to
293.2 KB, so a reader who never opens Architecture pays 3.4 KB. The rest of the
measurement is the problem:

| | before | after |
| --- | --- | --- |
| npm packages added | | 112 |
| JavaScript chunks in `dist` | 1 | 20 |
| largest added chunks | | 662 KB core, 435 KB cytoscape, 259 KB katex |
| total lazily loaded | | about 2.3 MB |

That is cytoscape and a maths typesetter, to draw two flowcharts, in a
repository that already renders hand-drawn SVG on its own zoomable pages
(ADR: Diagram pages). The fences stayed, the dependency went, and the reader in
the app gets the source with a line pointing at the drawing.

## In the code

The style job (`.github/workflows/ci.yml`):

```live path=.github/workflows/ci.yml region=style-job
```

## Consequences

- Formatting stops being a review topic. It is also no longer negotiable: the
  first commit after this one had to be reformatted by Prettier before it could
  pass, which is the system working.
- `dotnet format` reformatted a small number of C# files as a one-off. The diff
  was checked with `git diff --ignore-all-space` before it was accepted, and
  every one of the three suites was run after it.
- The `Program.cs` BOM is gone. The ship gate had a check asserting the BOM was
  present, added when an encoding-blind PowerShell edit once corrupted a file.
  That check now asserts the opposite, because the formatter is the thing
  guarding encoding now.
- A record can carry a diagram in text from here on, which the records for the
  database and the event stream will want.
- One deviation from the brief worth stating: the records stay at
  `docs/ADR-NNN-*.md` rather than moving to `docs/adr/NNNN-*.md`. Twenty-seven
  files, the served catalog, the sidebar and every cross-reference between them
  would move for a naming convention, with nothing gained that a reader can
  see. The numbering and the ordering are what make an index work, and both are
  already there.

## Files

- [`.github/workflows/ci.yml`](https://github.com/SteveStout/TheYard/blob/main/.github/workflows/ci.yml): the style job.
- [`.oxlintrc.json`](https://github.com/SteveStout/TheYard/blob/main/.oxlintrc.json), [`.prettierrc.json`](https://github.com/SteveStout/TheYard/blob/main/.prettierrc.json), [`.prettierignore`](https://github.com/SteveStout/TheYard/blob/main/.prettierignore): the three configs.
- [`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/.editorconfig): the C# half, now enforced by `dotnet format` rather than requested of an editor.
- [`package.json`](https://github.com/SteveStout/TheYard/blob/main/package.json): `lint`, `format`, `format:check`, `format:api`, `format:api:check`.
- [`docs/ARCHITECTURE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ARCHITECTURE.md): the topology and the layer direction, in Mermaid.
- [`docs/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): the half a tool cannot check.
- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the finding, fixed.
