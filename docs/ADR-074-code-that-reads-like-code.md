# ADR: Code that reads like code

**Status.** Accepted, 15 September 2026, shipped in 1.0.0.133.

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

```live path=src/components/DocsMenu.tsx region=code-renderer
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
| `--color-code-keyword` | `#6d4a83` | 6.37 |
| `--color-code-type` | `#2a5d8f` | 6.16 |
| `--color-code-string` | `#8a5a3c` | 5.22 |
| `--color-code-number` | `#2f6b3a` | 5.74 |
| `--color-code-comment` | `#63676e` | 5.10 |
| `--color-code-meta` | `#7a5230` | 6.12 |

Code sits on `--color-surface-muted`, so that is the ground each one is measured against, and 4.5 is
the floor because a keyword is normal text at 0.9em. `tokens.test.ts` reads the real stylesheet and
holds every one of those ratios, and it holds a second thing a ratio cannot: that no two of the six are
the same value and none of them is the prose color, because six colors that all clear AA and cannot be
told apart is a theme that passed a test and failed a reader.

```live path=src/styles/code.css region=code-theme
```

## What this does not do

- Bicep and Mermaid have no grammar here. Both render as escaped text.
- Nothing is highlighted on the server. The documents are still served as markdown, and a reader who
  fetches `/api/docs/<slug>` gets the same markdown they always did.
- The diagram pages are SVG and are untouched.

## Files

- [`src/lib/highlight.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/highlight.ts): the grammars, the aliases and the one function that turns code into HTML.
- [`src/lib/highlight.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/highlight.test.ts): what happens to a C# class, to a region that starts mid-file, and to a tag inside a sample.
- [`src/styles/code.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/code.css): the theme, in tokens.
- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the six colors.
- [`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts): the contrast floor and the told-apart rule.
- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the renderer.
- [`api/TheYard.Api/LiveSamples.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/LiveSamples.cs): the language a file name implies.
- [`api/TheYard.Tests/LiveSamplesTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/LiveSamplesTests.cs): the theory that holds those answers.
- [`tests/e2e/practices.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/practices.spec.ts): the browser check that a keyword and a comment are their own elements on the live page.
