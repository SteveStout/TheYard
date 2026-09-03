# ADR: The Decision Records index

Status: accepted, 2026-09-03, shipped as 1.0.0.39. Steve's ask, in full: "add a
new tab for ADRs."

## Context

The records were filed by topic, which was the right answer at eight of them
and the wrong one at twenty-seven. ADR: App Architecture section had grouped
them under the thing each one decided: hosting decisions under Hosting,
pipeline decisions under CI/CD, and everything else under Best Practices.
Everything else turned out to be most of them.

By last night Best Practices was eighteen rows deep, seventeen of which were
records, and the section had stopped being a menu. There was also no way to
answer "how many decisions are written down, and which one is number twelve"
without reading four sections and counting.

## Decision

**One section, every record, in the order they were decided.** The four topic
sections keep their overviews and nothing else. Hosting is now two rows.

**Numbered to match the file.** ADR-012 in the sidebar is
`docs/ADR-012-changelog.md` on disk. The number is a separate element beside
the label rather than part of it, which keeps the button's accessible name
exactly the label a reader hears and a test clicks.

**Collapsed by default, as a native `details`.** This was not the first
version. The first version had the section always open, and the screenshot of
it is why this paragraph exists: twenty-seven rows at the bottom of the rail
put the heading right on the fold above the pinned footer, so the new tab read
as empty until you scrolled past it. An index of twenty-seven is a disclosure.

`details` and `summary` rather than state and a button, because the open and
closed semantics, the keyboard handling, and the screen-reader announcement all
come from the element. There is nothing left to get wrong, and it is one fewer
piece of state.

## In the code

The section, and the numbering (`src/components/DocsMenu.tsx`):

```live path=src/components/DocsMenu.tsx region=records-menu
```

One shell for both kinds of section (`src/components/SideNav.tsx`):

```live path=src/components/SideNav.tsx region=section-shell
```

## Consequences

- The rail is short again. Four overviews, an index, the changelog, About.
- Six browser tests changed, because they used to click a record that was
  always visible and now open the index first. That is the interaction a reader
  performs, so the tests are closer to true than they were.
- A record is now reachable in two clicks from anywhere and one from the index,
  and the numbers make it possible to say "read 023 and 027" and be understood.
- The topic grouping is gone as navigation. It survives where it always
  mattered, in each record's own Files section and in the overviews that link
  them.
- Adding a record is now three lines in one place instead of a decision about
  which section it belongs to.

## Files

- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the sections, the record order, the numbers.
- [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx): the section shell and the disclosure.
- [`src/components/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.module.css): the summary's marker and the number's tabular figures.
- [`api/TheBlock.Tests/DocsCatalogTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheBlock.Tests/DocsCatalogTests.cs): the test that still holds the sidebar and the served catalog to the same slugs, unchanged by the move.
- [`docs/ADR-022-app-architecture-group.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-022-app-architecture-group.md): the grouping this replaces, and why it was right at the time.
