# ADR: The phone header

Status: accepted, 2026-09-02, shipped through the new pipeline as its first
real passenger.

## Context

The header grew four dropdown menus and an Admin tab in two days, and on a
phone they did not fit. The site is on the resume, so a recruiter's first
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

The sheet renders from the same record the dropdowns use
([`src/components/DocsMenu.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.tsx)):

```ts
/** Header order, left to right. The phone sheet lists its sections the same way. */
export const MENU_ORDER: MenuVariant[] = ['hosting', 'cicd', 'practices', 'about'];

/** Links that sit beside the docs, shared by the dropdowns and the phone sheet. */
export const LINKS = {
  ciRuns: { label: 'CI runs on GitHub', href: 'https://github.com/SteveStout/TheYard/actions' },
  resume: { label: "Steven's resume (PDF)", href: '/api/docs/resume' },
  repo: { label: 'GitHub repository', href: 'https://github.com/SteveStout/TheYard' },
} as const;
```

Opening a doc from the sheet closes the sheet and hands the request to the
one shared viewer ([`src/components/MobileDocs.tsx`](https://github.com/SteveStout/TheYard/blob/587c9e9/src/components/MobileDocs.tsx)):

```ts
const openSheet = () => {
  sheetRef.current?.showModal();
  setSheetOpen(true);
};
const closeSheet = () => sheetRef.current?.close();

const openDoc = (key: DocKey) => {
  closeSheet();
  setRequest((prev) => ({ key, nonce: (prev?.nonce ?? 0) + 1 }));
};
```

The header swaps surfaces with CSS alone; both sets of controls are always
in the tree ([`src/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/App.tsx) and
[`src/App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/App.module.css)):

```tsx
<div className={styles.desktopActions}>
  <DocsMenu menu="hosting" />
  <DocsMenu menu="cicd" />
  <DocsMenu menu="practices" />
  <DocsMenu />
  <button type="button" className={styles.adminTab} onClick={openAdmin}>
    Admin
  </button>
</div>
{bidCount > 0 && (
  <button type="button" className={styles.resetBids} onClick={handleResetBids}>
    Reset bids ({bidCount})
  </button>
)}
<MobileDocs onOpenAdmin={openAdmin} />
```

```css
/* Phones: the dropdowns and the Admin button give way to the hamburger sheet. */
@media (max-width: 639px) {
  .desktopActions {
    display: none;
  }
}
```

The doc viewer goes edge to edge on a phone
([`src/components/DocsMenu.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/DocsMenu.module.css)):

```css
@media (max-width: 639px) {
  .dialog {
    width: 100vw;
    max-width: 100vw;
    height: 100dvh;
    max-height: 100dvh;
    margin: 0;
    border-radius: 0;
  }
}
```

The proof ([`tests/e2e/mobile.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/mobile.spec.ts)):

```ts
test.use({ viewport: { width: 375, height: 812 } });
```

These excerpts were copied from the files at the commit that shipped them.
A follow-up decision covers keeping samples like these live rather than
copied.

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
[`src/components/SheetIcons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SheetIcons.tsx),
the drawer is
[`src/components/MobileDocs.tsx`](https://github.com/SteveStout/TheYard/blob/587c9e9/src/components/MobileDocs.tsx)
with its styles in
[`src/components/MobileDocs.module.css`](https://github.com/SteveStout/TheYard/blob/587c9e9/src/components/MobileDocs.module.css),
the palette lives in
[`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css)
with its proof in
[`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts),
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
[`src/components/SideNav.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.tsx)
with its styles in
[`src/components/SideNav.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/SideNav.module.css);
the links above that name the old files now point at 587c9e9, the commit
that shipped them, so they keep resolving.
