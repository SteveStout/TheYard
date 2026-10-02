# ADR: The phone header

Status: accepted, 2026-09-02, shipped through the new pipeline as its first
real passenger.

## In plain words

On a phone the header's dropdown menus did not fit, so below 640 pixels they give way to one button that opens a full-height list of every menu. Both views draw from the same menu data, so they cannot drift apart.

What that is worth: a developer changes a menu in one place and a phone-sized test proves it still works, and the organization's site reads as well on a phone as on a laptop.

## Context

The header grew four dropdown menus and an Admin tab in two days, and on a
phone they did not fit. The site is on the resume, so a visitor's first
look is as likely to be a phone as a laptop. His words on the problem: the
UI is not mobile friendly with all the dropdowns and must be fixed.

## Decision

Below 640 pixels the four dropdowns and the Admin button hide, and one
hamburger button opens a full-height sheet that lists every menu as a
headed section, then Admin, the resume, and the repository. Above 640
pixels nothing changes. The four choices inside that:

- **One data source.** The dropdowns already rendered from a `MENUS`
  record. Instead of a second copy for the phone, that record and the
  links beside it are exported and the sheet renders from them. The two
  surfaces cannot drift apart, because there is nothing to drift.
- **A native dialog for the sheet.** The browser's `<dialog>` element
  gives Escape, backdrop clicks, focus trapping and the top layer for
  free. No overlay library, no z-index arithmetic.
- **One doc viewer, shared.** The fetch-render-cache dialog that lived
  inside each dropdown moved into a `DocDialog` component. The dropdowns
  and the sheet both open docs through it, and on a phone the viewer takes
  the whole screen.
- **The proof is a phone-sized test.** A Playwright spec at 375 by 812
  pixels asserts the dropdowns are gone, the sheet lists all four groups,
  a doc opens full-screen from it, Admin is reachable, and the footer
  still renders. The desktop specs stayed untouched, which is how the
  desktop is proven unchanged.

## In the code

The samples below are read from this build's source each time the page is
served (ADR: Live code samples). The header half of this record was
superseded by ADR: The sidebar the same day, so what follows is the part
that still stands, shown as it is today.

The sidebar renders its sections from the same record the dropdowns once
used ([`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts)):

```live path=src/lib/siteMap.ts region=MENU_ORDER
```

Opening a doc from the drawer closes the drawer and hands the request to
the one shared viewer ([`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx)):

```live path=src/components/layout/SideNav/SideNav.tsx region=drawer-dialog
```

Below the docking line the header carries the brand, Reset bids, and the
hamburger; above it the rail makes a header redundant
([`src/app/Shell.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/Shell.tsx), drawn by [`src/app/Header.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/Header.tsx)):

```live path=src/app/Shell.tsx region=header-below-dock
```

The doc viewer goes edge to edge on a phone
([`src/library/DocDialog.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocDialog.module.css)):

```live path=src/library/DocDialog.module.css region=phone-dialog
```

The proof is [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts),
which runs at 375 by 812 pixels. The test tree sits outside the roots a
live block may read, so it is linked rather than shown.

## What this replaced

Nothing was removed. The alternative considered was a responsive dropdown
(wrap the four triggers onto a second row on phones), which keeps four
tiny menus a thumb cannot hit and does nothing for the docs dialog. A
second alternative, a separate mobile route, would have duplicated the
menu data, exactly the drift the one-data-source rule exists to prevent.

## Consequences

- Sixteen end-to-end checks now instead of thirteen; three of them run at
  phone size.
- Every new doc or menu entry appears on the phone automatically, since
  the sheet has no list of its own.
- The docs dialog and the sheet are both native dialogs, so opening a doc
  from the sheet closes the sheet first. A person taps the hamburger again
  to pick another doc, which is one tap and keeps the code simple.
- The breakpoint is 640 pixels because the layout already switched its
  padding there; one breakpoint is easier to reason about than two.

## Addendum, 2026-09-02: the sheet restyled to a reference

Shipped in 1.0.0.14, the same day as the original. Steve pointed at a
reference, mentor/reference-mobile-menu.png in the working folder: a dark
full-height panel, an icon leading every row, tall rows, muted section
headers. The pattern was taken and the palette was not. The mockup is
orange; The Yard stays navy and silver.

What changed:

- **A drawer instead of a full sheet.** The panel slides in from the left,
  at most 360 pixels wide, over a dimmed page, so a person keeps their
  place. It is still the one native dialog, so Escape, the backdrop and the
  X close it exactly as before.
- **An icon per row kind, six in all**: overview document, decision record,
  infrastructure, external link, Admin, changelog. Every doc in the shared
  record declares its kind and the row picks its icon from that. Six small
  inline SVGs reused across two dozen rows, none bespoke.
- **Touch targets of 48 pixels**, above the 44 the guidelines ask for, with
  a hairline divider between rows the way the reference draws them.
- **Contrast measured, not guessed.** The light text on the navy ground and
  on the raised hover row, and the muted header shade on both, were
  computed before being chosen: text 14.4:1 and 11.8:1, muted 6.3:1 and
  5.2:1, all past the 4.5:1 that WCAG AA asks for normal text. The brand
  blue itself measures 3.1:1 against the navy, so the drawer uses a lighter
  blue for active icons and the focus ring. A unit test reads the tokens
  and asserts the ratios, so a future shade change cannot slip under AA.
- **The desktop header now renders from MENU_ORDER** instead of four
  hand-placed dropdowns, so the Changelog menu (ADR: The changelog) appeared
  on both surfaces from one line of data. The dropdowns themselves are
  unchanged, and the desktop specs still say so.

In the code: the icon set is
[`src/components/shared/SheetIcons/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SheetIcons/SheetIcons.tsx),
the drawer is
[`src/components/MobileDocs.tsx`](https://github.com/SteveStout/TheYard/blob/587c9e9/src/components/MobileDocs.tsx)
with its styles in
[`src/components/MobileDocs.module.css`](https://github.com/SteveStout/TheYard/blob/587c9e9/src/components/MobileDocs.module.css),
the palette lives in
[`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css)
with its proof in
[`src/styles/colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts),
and the phone spec
[`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts)
now asserts the Changelog section, an icon on every row, and the row height.
The header block excerpted above under "In the code" is the pre-addendum
shape; today it reads:

```tsx
<div className={styles.desktopActions}>
  {MENU_ORDER.map((menu) => (
    <DocsMenu key={menu} menu={menu} />
  ))}
  <button type="button" className={styles.adminTab} onClick={openAdmin}>
    Admin
  </button>
</div>
```

## Note, 2026-09-02, later the same day: superseded on the desktop

Shipped as 1.0.0.15, the dropdowns are gone at every width and the drawer
above became one shape of the sidebar. The record of that decision is
ADR: The sidebar, under Best Practices. What this record still owns: the
one-data-source rule, the native dialog, the shared viewer, the phone-sized
proof, and the palette and icon rows from the addendum. The component moved
from `src/components/MobileDocs.tsx` to
[`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx)
with its styles in
[`src/components/layout/SideNav/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.module.css);
the links above that name the old files now point at 587c9e9, the commit
that shipped them, so they keep resolving.

## Note, 2026-09-02, evening: the dark palette retired

1.0.0.19 made the sidebar light at Steve's request ("the side panel is too
dark, keep everything light and modern"); ADR: The sidebar records the new
palette. The icon rows, the 48-pixel targets and the measure-before-choosing
rule from the addendum above stand; only the colors changed.

## Files

- [`src/components/layout/SideNav/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.tsx) and
  [`src/components/layout/SideNav/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/SideNav/SideNav.module.css): the drawer, now one shape of
  the sidebar (ADR: The sidebar).
- [`src/components/shared/SheetIcons/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SheetIcons/SheetIcons.tsx): the icon per row kind.
- [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts): the one data record both shapes
  render from.
- [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css) and [`src/styles/colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts):
  the palette and its measured contrast.
- [`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts): the 375 by 812 proof.
- [`index.html`](https://github.com/SteveStout/TheYard/blob/main/index.html): the viewport meta that makes a phone a phone.

## The look, from the live site

![A 375 pixel phone: the header with the brand and the hamburger, the inventory below](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-phone-home.jpg)

![The drawer open on the phone: icon-led rows under muted section headings](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/app-phone-drawer.jpg)

The icon per row kind, seven paths that cover every row
([`src/components/shared/SheetIcons/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/shared/SheetIcons/SheetIcons.tsx)):

```live path=src/components/shared/SheetIcons/SheetIcons.tsx region=icons
```
