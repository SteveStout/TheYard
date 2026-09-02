# ADR: The sidebar

Status: accepted, 2026-09-02, shipped as 1.0.0.15. Supersedes the desktop
half of ADR: The phone header; the phone half lives on inside this one.

## Context

The header had grown five dropdown menus (Hosting, CI/CD, Best Practices,
Changelog, About) plus an Admin button, and on a phone they gave way to a
drawer. Two navigation surfaces for one site, and the desktop one was the
weaker: a hiring manager landing on a laptop had to guess which of five
small words hid the decision records. Steve's words, with a reference
attached: "I wanted a sidebar instead of drop downs", and "the drop downs
look horrible on mobile". The reference was the Dribbble mobile-sidebar
pattern: one dark side panel, a brand block at the top, icon-led rows under
muted headers, everything in one place.

## Decision

One navigation surface at every width, built from the same MENUS record the
dropdowns and the drawer already shared. The dropdowns are deleted.

- **At 1024 pixels and up the panel docks** as a persistent left rail, 272
  pixels wide, beside the page. A collapse control shrinks it to a 64 pixel
  icon rail; the labels stay in the accessibility tree and return as
  tooltips, and the choice is remembered per browser in localStorage. The
  page keeps its 1200 pixel content width inside the remaining column.
- **Below 1024 pixels the same panel is the drawer** from the previous
  record: a hamburger in the header opens it over the dimmed page, and
  Escape, the backdrop, or the X closes it.
- **The header goes away when the rail is docked.** The rail's brand block
  is the way home, so a second brand in a header would have been a
  duplicate. Below 1024 pixels the header stays, carrying the brand, Reset
  bids, and the hamburger.
- **Admin and Reset bids move into the rail's pinned group**, with the
  resume and the repository, so every action lives in the one panel. Admin
  keeps its ?view=admin address and its row reads as current while the tab
  is showing; a doc's row reads as current while that doc is open.
- **1024 is the docking line** because a 272 pixel rail beside the 760
  pixel doc viewer needs the room, and because 640 to 1023 covers tablets
  and narrow windows where a drawer is the honest answer.
- **The cost was paid in tests, not in taste.** Six desktop checks that
  opened docs through dropdown menu items were rewritten to open them
  through the rail; a new sidebar check covers the docked shape, the
  collapse, the memory of it, and the 1023 to 1024 boundary. The five phone
  checks were kept as they were, which is how the drawer is proven unchanged.

## In the code

The samples below are read from this build's source each time the page is
served (ADR: Live code samples). The one component and its two shapes
([`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx)):

```live path=src/components/SideNav.tsx region=shapes
```

The docking line, read by the app as a media query, and the rail's memory
([`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx)
and [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts)):

```live path=src/App.tsx region=docking
```

The layout, two columns with the rail's width from a token
([`src/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/App.module.css)
and [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css)):

```live path=src/App.module.css region=rail-grid
```

```live path=src/styles/tokens.css region=rail-widths
```

The rows, the icons, and the palette are unchanged from the phone record:
[`src/components/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.module.css),
[`src/components/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SheetIcons.tsx),
and the contrast proof in
[`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts).
The proof of the docked shape is
[`tests/e2e/sidebar.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/sidebar.spec.ts).

## What this replaced

The dropdown component and its styles were deleted outright rather than
kept behind a flag. The MENUS record, the doc viewer, and the phone drawer
were kept; the drawer was renamed into the sidebar component because it
already was the sidebar, only shown on phones.

The alternative considered was a hamburger at every width with no docked
rail. It matches the reference shots more literally and costs a breakpoint
less, but it hides the documents behind a tap on the widest screens, which
is where a hiring manager lands first. The rail keeps them one glance away.

## Consequences

- Five menus became one panel; a new doc or section appears in the rail and
  the drawer from one line of data, as before.
- Twenty-four end-to-end checks, five of them at phone size and five in
  the new sidebar file. The desktop specs read simpler than they did, because a
  rail row is one click where a dropdown was two.
- The header exists only below 1024 pixels now. Anything added to it later
  needs a home in the rail as well, which the pinned group provides.
- The collapsed rail depends on tooltips for labels, so it is a power-user
  shape; the default is open, and the memory is per browser.

## Addendum, 2026-09-02: light, on Steve's word, shipped as 1.0.0.19

Steve's words, from his phone, after seeing 1.0.0.17: "the side panel is
too dark, keep everything light and modern". The dark navy panel came from
the reference in ADR: The phone header's addendum; the rest of the site
never followed it, so the sidebar was the one dark surface on a light
page. It is light now.

What changed, all of it in the tokens and one stylesheet:

- **White ground, a hairline instead of a shadow.** The rail is white with
  a one-pixel border on its page side; the drawer stays white over a
  lighter dim. The brand and the current row read in the heading navy,
  and the brand blue marks active icons, the focus ring and the current
  row's edge.
- **Rows without dividers.** The hairline under every row is gone; a row
  is a rounded highlight that appears on hover and stays on the current
  one, the shape most light sidebars use. Rows are still 48 pixels tall
  with an icon leading each.
- **Contrast measured again.** Text 15.1:1 on white and 13.6:1 on the
  tinted row, muted text 5.9:1 and 5.3:1, icons 4.8:1 and 4.4:1, the
  brand blue 5.1:1 and 4.5:1. The unit test that guarded the dark palette
  guards this one; only the numbers it reads changed.

The palette, read from this build
([`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css)):

```live path=src/styles/tokens.css region=sheet-tokens
```

Nothing moved: the rows, the icons, the docking line, the collapse and the
drawer are as this record describes above, and the twenty-four end-to-end
checks passed unchanged.

## Files

- [`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx) and
  [`src/components/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.module.css): the one navigation surface in
  its two shapes.
- [`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx) and [`src/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/App.module.css): the docking line,
  the rail grid, and the header that exists only below it.
- [`src/hooks/useMediaQuery.ts`](https://github.com/SteveStout/TheYard/blob/main/src/hooks/useMediaQuery.ts): how the docking line is read.
- [`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx): the sections, in `MENU_ORDER`.
- [`src/components/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SheetIcons.tsx) and
  [`src/components/BrandMark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/BrandMark.tsx): the row icons and the brand.
- [`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css): the rail widths and the sheet tokens.
- [`tests/e2e/sidebar.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/sidebar.spec.ts) and [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts):
  the proof at 1280, 1023 and 375.

## The look, from the live site

Captured from the domain at 1.0.0.22 by the same headless Chrome the
end-to-end suite uses (ADR: Docs and testing, addendum).

![The docked rail beside the inventory at 1280 pixels: sections of icon rows, Admin and the links pinned below](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-home.jpg)

![The rail collapsed to a 64 pixel icon column, the page taking the width](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-rail-collapsed.jpg)

![The drawer on a 375 pixel phone: the same rows, sliding in over the dimmed page](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-phone-drawer.jpg)

The rows themselves, one loop over the shared record
([`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx)):

```live path=src/components/SideNav.tsx region=rows
```
