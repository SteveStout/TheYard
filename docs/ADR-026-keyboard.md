# ADR: Keyboard and screen reader

Status: accepted, 2026-09-03, shipped as 1.0.0.36. The README listed this as
open work: "focus management on view switches (the detail page should receive
keyboard focus), plus a fuller accessibility audit."

## Context

The app was already better than its starting point in the parts a component
library gives you for free. The palette clears WCAG AA on both grounds and a
test asserts every pair (ADR: The palette). The sidebar's drawer is a native
`<dialog>`, so focus trapping, the backdrop, and Escape are the browser's job
rather than a hand-rolled approximation. Icons are `aria-hidden`, icon-only
buttons carry labels, and the document rows mark the open one.

Three things were still wrong, and all three are the same kind of wrong: they
are invisible to a mouse.

**Nothing moves focus when the view changes.** Clicking a tile replaces the
grid with the detail page. The tile that had focus no longer exists, so focus
falls to `<body>`: the next Tab starts over at the top of the document, and a
screen reader announces nothing, because from its point of view nothing
happened. Back to the list, and into the Admin tab, behave the same way.

**The rail is thirty buttons deep.** Every document, every record, the Admin
link. A keyboard user reaches the vehicle grid by tabbing past all of it, and
does it again after every view change, because of the paragraph above.

**Filtering is silent.** Narrowing a hundred thousand vehicles to eleven
rerenders the grid and says nothing. A sighted user reads the count in the
filter bar; nobody else is told it changed.

## Decision

**A skip link, first in the tab order.** Visible only while focused, which is
the whole convention: a mouse user never sees it, a keyboard user finds it
first. It is positioned off-screen rather than `display: none`, because a
`display: none` element cannot take focus and the link would do nothing.

**Focus follows the view.** `<main>` carries `tabIndex={-1}`, which makes it
focusable by script and never by Tab, and an effect moves focus there when the
view identity changes: list, a specific vehicle, or the Admin tab. It fires on
change only, never on first paint, because the browser's own initial focus is
correct. `preventScroll` is passed because the scroll-restore effect already
decides where the page sits, and the two otherwise fight.

**A polite live region says what changed.** One visually hidden `role="status"`
paragraph, whose text is derived from the same state the view is: the vehicle's
name on the detail page, the match count on the list, the loading and error
states in between. Polite by definition, so it waits for a pause rather than
interrupting.

## In the code

The focus effect, keyed on view identity rather than on any one piece of state
(`src/App.tsx`):

```live path=src/App.tsx region=focus
```

The announcement, derived rather than pushed, so it cannot go stale:

```live path=src/App.tsx region=announcement
```

The link itself, and the element it targets:

```live path=src/App.tsx region=skip-link
```

The two rules that make a hidden thing audible and a focused thing visible
(`src/App.module.css`):

```live path=src/App.module.css region=a11y
```

## Consequences

- Tab from a cold load reaches "Skip to content" first, then the rail. The
  order is deliberate: the link is the escape hatch, so it comes before the
  thing it escapes.
- Opening a vehicle, going back, and opening Admin each move focus to the new
  view and announce it. The Playwright suite asserts all three, because focus
  is exactly the kind of behaviour that a refactor breaks silently.
- The live region adds a paragraph to the DOM that no sighted user will ever
  see. That is the cost of the feature and it is the right cost.
- Still open: a full audit with an actual screen reader, which is a person's
  job and not a checklist's. The keyboard path is now walkable end to end,
  which is the part a test can hold.

## Files

- [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx): the skip link, the focus effect, the live region.
- [`src/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/App.module.css): the two rules that make them work.
- [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx): the drawer's native dialog, and `aria-current="page"` on the open document.
- [`tests/e2e/a11y.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/a11y.spec.ts): the keyboard path, walked.
- [`docs/ADR-016-palette.md`](https://github.com/SteveStout/TheYard/blob/main/docs/ADR-016-palette.md): the contrast half of this, decided earlier and asserted in `src/styles/tokens.test.ts`.
