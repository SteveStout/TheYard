# ADR: An App Architecture section in the sidebar

Status: accepted, 2026-09-02, shipped as 1.0.0.28. Steve's ask: "make sure
everything on the UI is grouped and well organized so we need a new header
or App Architecture for the code ADR."

## Context

The sidebar grew by accretion. Best Practices ended the day holding
fifteen entries: the practices overview, the records about documentation,
observability, the phone header, the changelog, the sidebar itself, live
samples, cache headers, the palette, the staff review, diagram pages, and
then the three records that walk the code for a new developer. Two
different kinds of thing were filed together: decisions about how the
project is run, and explanations of how the application is built. About
held the README, the data flow and the project structure, which are the
second kind, filed under a heading that suggests the first.

A visitor looking for "how is this built" had to know that the answer was
split across About and the bottom of Best Practices. That is a grouping
problem, and it hid the newest and most useful writing in the repository.

Two documents the README promised at the start of the build were also
still missing: a written architecture (the onion, the wire contract, the
derive-do-not-store principle) and a written style (naming, layering, and
comments that explain why and how, never what), enforced by an
`.editorconfig` rather than by memory. There was no section they belonged
to either.

## Decision

**A section of its own, named for what a visitor is looking for.** App
Architecture sits first in the sidebar, above Hosting, and holds
everything about how the application is built:

- Architecture overview, the new `docs/ARCHITECTURE.md`: the layers and
  which way they point, the seven rules that keep them, a table of where a
  change goes, and what is deliberately absent.
- Coding and commenting style, the new `docs/STYLE.md`: naming, layering,
  the why-and-how comment rule with an example of a comment that earns its
  line, the test rules, and the formatting the `.editorconfig` enforces.
- Data flow and Project structure, moved out of About.
- The three records that walk the code: Program.cs, the React
  configuration, and the tests.

**About holds the README.** It is the introduction to the project, not the
place to file its internals. The resume link stays pinned below the
sections, where it already was.

**Best Practices keeps the decisions about how the work is done**:
versioning, documentation and testing, observability, error handling, the
phone header, the changelog, the sidebar, live samples, cache headers, the
palette, the staff review, diagram pages, and this record. Every entry is
about practice, none about the code's shape.

**`.editorconfig` does the mechanical half.** Indentation, line endings,
using order, `var` usage, braces, unused locals: a tool applies those on
every save and the compiler warns on them, so review spends its attention
on the half a tool cannot check.

## In the code

The one record of every document the sidebar can open, and the section
order (`src/components/DocsMenu.tsx`):

```live path=src/components/DocsMenu.tsx region=architecture-menu
```

```live path=src/components/DocsMenu.tsx region=MENU_ORDER
```

The mechanical style rules (`.editorconfig`):

```live path=.editorconfig region=*
```

## Consequences

- The sidebar's first section now answers the question most visitors
  arrive with, and each section holds one kind of thing.
- Three end-to-end tests name the sections; all three were updated, and
  two of them assert the new section renders in the rail and in the phone
  drawer.
- The rail is taller by one section. At 1024 pixels and up it scrolls
  inside itself, which it already did; on a phone the drawer scrolls.
- Moving a record between sections does not change its address. The slug
  in `DocsCatalog.cs` is the identity; the menu is presentation only, and
  the cross-check test still holds the two lists together (ADR: The staff
  review).

## Files

- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the sections, their order, and the document record.
- [`api/TheYard.Api/DocsCatalog.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/DocsCatalog.cs): the two new slugs.
- [`docs/ARCHITECTURE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ARCHITECTURE.md) and [`docs/STYLE.md`](https://github.com/SteveStout/TheYard/blob/main/docs/STYLE.md): the two documents this section was made for.
- [`.editorconfig`](https://github.com/SteveStout/TheYard/blob/main/.editorconfig): the mechanical rules.
- [`tests/e2e/sidebar.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/sidebar.spec.ts), [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts), [`tests/e2e/smoke.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/smoke.spec.ts): the section names and the two new pages, checked in a browser.
- [`docs/ADR-013-sidebar.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-013-sidebar.md): the sidebar itself, which this record only regroups.
