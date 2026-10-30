# ADR: Code that reads like code

Status: accepted, 2026-09-15, shipped as 1.0.0.133.

## In plain words

This page decides how the code samples on the site's pages get coloured by meaning (syntax highlighting): one small library (highlight.js) carrying only the languages the pages use, drawn in the site's own palette, with a test holding every colour to a readable contrast.

What that is worth: a developer reads a sample the way an editor would show it, with keywords told apart from comments. The organization shows code worth reading on its public pages without slowing the inventory page, because the renderer arrives with the first document.

## The problem, measured before it was solved

Every document this site serves renders through `marked`, which hands a fenced block back as plain
text. The result is that a hundred and eighty C# samples across the records, the architecture pages and
the Best Practices section were the same color as the sentence above them, on a site whose whole
argument is that the code is worth reading.

The languages were counted rather than guessed, across all eighty-eight documents the catalog serves:
179 C# blocks (32 written by hand, 147 expanded from the build), 59 TypeScript and TSX, 16 YAML, 8
JSON, 7 CSS, 5 SQL, 3 Mermaid, 2 Bicep, and one each of Python, Dockerfile, TOML and HTML.

## What was considered

| Option | What it buys | What it costs |
| --- | --- | --- |
| **highlight.js, this site's palette** | Real grammars for every language above, a theme written in the site's own tokens, one runtime dependency | About 45 KB gzipped in the bundle, and a palette that has to be held to AA like the rest |
| A tokenizer written here | No dependency at all, total control | C# has verbatim strings, interpolation, generics and attributes; a wrong color on a public page reads worse than no color |
| Shiki | The same grammars a code editor uses, the best fidelity available | A WebAssembly engine and grammar files over a megabyte, or a build step that pre-renders every document, against documents that render at request time |
| Leave it plain | Nothing to maintain, no dependency, no palette risk | The code stays monochrome for every reader who opens a record on a phone |

**Decision: highlight.js, registered for the measured languages only, themed in the site's own tokens.**
The dependency is the third in the whole frontend, after React and `marked`.

## How it works

The grammars are imported one by one rather than as the whole library, so the bundle carries what the
documents use and nothing else:

```live path=src/lib/highlight.ts region=languages
```

A fence names a language, the name is resolved against the grammars this bundle holds, and anything
unresolved is escaped and rendered as text. Bicep and Mermaid are exactly that case today: the
expander still names them, no grammar reads them, and a reader sees plain code rather than an error.

```live path=src/lib/highlight.ts region=highlight
```

`marked` is handed one renderer, which is the only place a fence becomes markup:

```live path=src/lib/markdown.ts region=code-renderer
```

The expander names the language from the file's name, and two of the files the records show had no
answer before this change: a Dockerfile has no extension and `.editorconfig` is nothing but one, so
both rendered as plain text. `LiveSamples.LanguageFor` now answers for those, for TOML, Python,
JavaScript, HTML and PowerShell, and a theory in `LiveSamplesTests` holds every one of those answers.

## The palette, and why it is measured

The theme is six colors from the Urban slate family rather than a stock light theme, because a stock
theme arrives from another design and sits next to this one badly.

| Token | Value | Contrast on the code ground |
| --- | --- | --- |
| `--color-code-text` | `#332f2c` | 11.90 |
| `--color-code-keyword` | `#6d4a83` | 6.37 |
| `--color-code-type` | `#1f5c8b` | 6.36 |
| `--color-code-string` | `#8a5a3c` | 5.22 |
| `--color-code-comment` | `#25663a` | 6.20 |
| `--color-code-number` | `#96363b` | 6.52 |
| `--color-code-meta` | `#6b5a1e` | 6.07 |

Code sits on `--color-surface-muted`, so that is the ground each one is measured against, and 4.5 is
the floor because a keyword is normal text at 0.9em. `tokens.test.ts` reads the real stylesheet and
holds every one of those ratios, and it holds a second thing a ratio cannot: that no two of the six are
the same value and none of them is the prose color, because six colors that all clear AA and cannot be
told apart is a theme that passed a test and failed a reader.

```live path=src/styles/code-highlight.css region=code-theme
```

## Addendum, the same afternoon: what the owner saw

The first version of this theme put comments in slate gray and left the code itself in the prose
colour. Read on the live page rather than in a swatch, both were wrong. Comments are green in every
editor this project's reader has ever used, and gray comments on a gray ground read as disabled text
rather than as commentary. The code itself needed to be darker than the prose, because it sits on
`--color-surface-muted` rather than on white and 6.4 against that ground reads thin at 0.9em.

So the palette gained `--color-code-text` at 11.9 against the code ground, comments moved to green,
numbers moved off green to brick so that two token types are not one colour, and attributes moved to
olive. The table above is the current set and `tokens.test.ts` holds every ratio in it, plus a floor
of 7 for the code colour itself. **The lesson, and it is the one this project keeps relearning: a
palette is read on the page it ships to, not in the file it is written in.**

## What this does not do

- Bicep and Mermaid have no grammar here. Both render as escaped text.
- Nothing is highlighted on the server. The documents are still served as markdown, and a reader who
  fetches `/api/docs/<slug>` gets the same markdown they always did.
- The diagram pages are SVG and are untouched.

## Where it sits

This sits almost entirely in the front end: src/lib/highlight.ts and src/lib/markdown.ts import nothing from React, and the host Api's only part is LiveSamples.LanguageFor naming a fence's language from a file name. Single responsibility, one reason to change, holds here because markdown.ts is the one file that imports marked and the highlighter, so a change of renderer touches one file and the dialog loads it on demand. It cost about 45 KB gzipped in its own chunk and a palette that tokens.test.ts must hold to AA contrast. If the documents were rendered at build time instead of request time, Shiki's editor-grade grammars would be worth their megabyte.

## Files

- [`src/lib/highlight.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/highlight.ts): the grammars, the aliases and the one function that turns code into HTML.
- [`src/lib/highlight.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/highlight.test.ts): what happens to a C# class, to a region that starts mid-file, and to a tag inside a sample.
- [`src/styles/code-highlight.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/code-highlight.css): the theme, in tokens.
- [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css): the six colors.
- [`src/styles/colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts): the contrast floor and the told-apart rule.
- [`src/lib/markdown.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/markdown.ts): the renderer, loaded with the first document.
- [`src/lib/markdown.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/markdown.test.ts): the rendered shape of a fence and a link.
- [`src/library/DocDialog.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocDialog.tsx): the dialog, and the import that fetches the renderer on demand.
- [`api/TheYard.Api/Features/Docs/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Docs/LiveSamples.cs): the language a file name implies.
- [`api/TheYard.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSamplesTests.cs): the theory that holds those answers.
- [`tests/e2e/practices.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/practices.spec.ts): the browser check that a keyword and a comment are their own elements on the live page.

## Addendum, 2026-09-17: the renderer arrives with the first document

Steve, 9/17: the fastest site at no extra cost. The measurement that opened
that work (perflane, `mentor\logs\lane0917-963-perflane-measure.log`) put the
whole frontend in one chunk, and `marked` and the highlighter with its
sixteen grammars were the largest things in it after React itself. Every
visitor downloaded and parsed the renderer on the inventory page, where no
document is ever rendered.

The two libraries now live in `src/lib/markdown.ts`, the one file that
imports either, and the documents dialog imports that file on demand the
first time a reader opens a record. Vite turns the dynamic import into a
chunk of its own, named by its hash like every other bundle file, so the
inventory page's script no longer carries the renderer and a reader who
opens a document fetches it once and keeps it for a year (ADR: Cache
headers). The renderer's two settings, the link hook and the code renderer
shown above, moved with it unchanged; the live block above reads them from
their new file. `markdown.test.ts` holds the rendered shape end to end: a
fenced block comes back highlighted under its grammar's class, a fence in a
language nobody here reads is escaped, and the links are rewritten the way
the dialog needs them.

The before and after are on the Performance page, measured on the live
site: the bytes the inventory page's script carried, and the bytes it
carries now, with the renderer's chunk beside it.

```live path=src/library/DocDialog.tsx region=renderer-on-demand
```

## Addendum, 2026-09-28 (1.0.3.34): code.css is code-highlight.css

The code theme is `src/styles/code-highlight.css`, named for what it does (ADR-016, the addendum on the four token files). Its rules are unchanged; the six code colours it draws in are in `colors.css`.

## Addendum, 2026-09-28 (1.0.3.35): the renderer and the Admin tab are fetched ahead

The renderer arriving with the first document had a cost the 17 September addendum did not measure: the order. Read on the live site on 28 September, opening a document fetched the markdown, and only when the markdown had arrived (775 ms in) did the renderer's chunk start (781 ms): a waterfall, the reader waiting on two trips in a row. The Admin tab and each of its cards have the same shape, a chunk fetched on the click that needs it.

- **The document and its renderer start together.** The dialog asks for the document, the renderer and, for the Author page, its layout in one `Promise.all`, so the two trips overlap rather than follow each other. `prefetch.spec.ts` holds the overlap: with the document held back half a second, the renderer's request has to begin before the document's response ends.
- **Fetched ahead, when the page is idle.** Once the first page has loaded and the browser is idle (`requestIdleCallback`, or a short timer where a browser has none), the page fetches the renderer and the Admin tab's chunk; once the Admin tab has mounted, it fetches every card's chunk in the rail the same way (`src/lib/prefetch.ts`). A reader whose browser asks to save data, or rates the connection as 2G, gets none of it, which `prefetch.test.ts` holds. The browser suite opens every page as such a reader (`tests/e2e/app.ts`), because the specs that hold a card fetched when it is opened, and a chunk a deploy replaced loading the page again, are about that very fetch; `prefetch.spec.ts` turns the prefetch back on and holds it. None of it is on the first page's critical path: it starts after the page's `load` event.
- **What a card waits on now.** With every card's chunk already in the browser, choosing a card waits on its first data read and not on its code, so the read is not started any earlier than the card's own `useRead` starts it: there is no second request for the same address to race the first.

## Addendum, 2026-10-30 (1.0.4.125): the rule has a body, and it is asked in two strengths

The rule that a comment says what, how and why on its own was held by one test over one area, the
six onion projects and the frontend under `src`, and the whole job hunt's Function sat outside it.
That gap is the reason the comments there had drifted: seventeen of them explained by a date or by
who asked, and nothing in the build could tell. The arrears are paid in this version, but the gap is
the fix.

**The body is `tests/rules/HistoryRule.cs`**, compiled into both test projects by a link the way
`LengthRule` and `SummaryRule` are, so what the suite refuses and what `.github/quality.py` counts
cannot drift into describing two different codebases. The question is asked by
`ProductionCommentTests`, a file of its own now: the rule had grown a second area and four theories
while living beside the em dash scan, which is two jobs in one file, and a file is cut at the warning
and not at the cliff.

**It is asked in two strengths, and the reason is in the code rather than in a preference.** The
site's own code has no reason to write a month of the year or the owner's first name, so there both
are provenance and nothing else. The job hunt's Function reads his inbox, his calendar and his
tracker, so his name there is the subject the code acts on; and its clock turns over when the clocks
do, so the word November in it is the calendar. A rule strong enough for the site would have forced
`November clock change` and `Sends one email from Steve's Gmail` to be rewritten into something less
true, which is how a style rule starts costing more than it returns. What is provenance in both is a
date, a version number, a named review, and his name beside a date or beside an ask.

**A date can be the subject, and the measurement had not known that.** Four of the seventeen the
ratchet was counting were not history at all: a comment naming the wire format a function parses has
to show it, and showing it means writing a date, so `` a day as `2026-10-12` `` and `"Status:
accepted, 2026-09-02, shipped as 1.0.0.21."` are examples of a format. The test had always known
this, by stripping backticked and quoted spans before it read a line; the scanner had not, and read
one regular expression over the raw line. Both now strip, and the handful the stripper cannot reach
are named one by one: a quotation that runs across two source lines, where a quote has no other half
to pair with, and a month written in code rather than in prose.

**A date that spells its year out is naming a fixed point, not a past.** A recent date in this
project's own history is written the way a person writes one, `23 Oct` or `7 October`, because the
year is this one, so the day-and-month form counts only when no four-digit year follows it. The one
date in this repository that spells its year out is `1 January 1970 UTC` in The Shed, the Unix epoch,
and a comment may legitimately need it.

The rule, from the build:

```live path=tests/rules/HistoryRule.cs region=history-rule
```

What the sample under `samples/` is held by is the ratchet's own count rather than this suite, which
does not walk a sample: a sample is its own solution with its own gate and its own copies of these
rules (`Repo.NotOurs`). A comment of this shape added there turns `history_or_name_comments` red
instead, which is weaker than a named failure and is written down here rather than left to be
discovered.
