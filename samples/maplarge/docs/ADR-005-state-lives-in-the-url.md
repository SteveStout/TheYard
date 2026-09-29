# ADR: State lives in the URL

Status: accepted, 2026-09-29.

## Context

The brief: "the application should be deep-linkable (the state of the UI should be kept in the
URL)." The easy version keeps state in the page and writes some of it to the address when it
remembers to. The version that satisfies the sentence keeps none of it anywhere else.

## Decision

The page's state is six values, and all six live in the query string:

| Key | Values | Meaning |
| --- | --- | --- |
| `view` | `browse`, `docs` | which tab the dialog shows |
| `path` | a folder, relative to home | the folder shown, or where a search starts |
| `q` | text | a search; present means search mode |
| `sort` | `name`, `size`, `modified` | the column the table is sorted on |
| `dir` | `asc`, `desc` | which way |
| `doc` | a slug | the document open on the Docs tab |

`src/lib/urlState.ts` is the one parser and the one serializer. `parse` reads a query string into a
state, dropping keys it does not know and putting a value outside its list back to the default;
`serialize` writes only what differs from the defaults, so a link is as short as it can be. Both are
pure and tested with `node --test`, no browser required.

The dialog is open when the address carries any known key, and closed when it carries none. A bare
address shows the page with the dialog closed; a link with `?path=reports/2026` opens straight into
that folder. Opening the browser from its button writes `?view=browse`, the shortest open state, so
the address always says what the page shows.

Every change goes through one function, `navigate` in `main.ts`: merge the change into the state,
write the address with `pushState`, then redraw from the address. Back and forward fire `popstate`,
which redraws from the address the same way. Closing the dialog navigates to the bare address, so
Back from a closed page reopens it where it was. The first keystroke of a search pushes an entry and every
keystroke after it replaces that entry, so Back returns to the folder without the search and
never steps through it letter by letter.

Two things are deliberately not in the URL. The listing itself is fetched, and cached by path for
the life of the page so Back is instant (ADR-006). The scroll position is the browser's.

## What it cost

A state that lives only in the address cannot hold anything the address cannot: a half-typed
folder name, an upload in flight. Those are transient and belong to the control that owns them.
The rule is that anything a person would want to come back to is in the address, and the test of
the rule is that every view has a link on the front page.

## Files

- [`src/lib/urlState.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/lib/urlState.ts): the parser and the serializer.
- [`src/main.ts`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/src/main.ts): `navigate`, the one way state changes.
- [`tests/js/urlState.test.js`](https://github.com/SteveStout/TheYard/blob/main/samples/maplarge/tests/js/urlState.test.js): the round trip and the fallbacks.

The parser:

```live path=src/lib/urlState.ts region=parse
```

The one way the state changes:

```live path=src/main.ts region=navigate
```
