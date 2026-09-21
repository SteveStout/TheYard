# ADR: The sidebar

Status: accepted, 2026-09-02, shipped as 1.0.0.15. Supersedes the desktop
half of ADR: The phone header; the phone half lives on inside this one.

## Context

The header had grown five dropdown menus (Hosting, CI/CD, Best Practices,
Changelog, About) plus an Admin button, and on a phone they gave way to a
drawer. Two navigation surfaces for one site, and the desktop one was the
weaker: a hiring manager landing on a laptop had to guess which of five
small words hid the decision records. Steve's words, with a reference
attached: "I wanted a sidebar instead of dropdowns", and "the dropdowns
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
- Twenty-five end-to-end checks, five of them at phone size and five in
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
drawer are as this record describes above, and the end-to-end
checks passed unchanged.

## Addendum, 2026-09-15: a twelfth section, for the API

The API describes itself from this version (ADR: The API describes itself), and the page a person
reads that description on is not markdown, so it cannot be a document row. It is a section of its
own, **API Reference**, right under App Architecture, holding two link rows that open in a new tab
the way the drawings do: the reference page and the OpenAPI document itself. A section rather than
two rows inside App Architecture because a reader who came to see the API should find it in the
table of contents without opening anything. The browser suite's count of headings is twelve now.

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

## Addendum, 2026-09-09: two sections, for the stores and for the drawings

Two sections joined the top of the sidebar. **SQL vs Cosmos DB** holds the
page that puts the two stores side by side (`docs/SQL-VS-COSMOS.md`); it
stands beside Hosting because the subject is as large as where the site runs,
and a reader should not have to know which record holds the comparison. The
records the page draws on stay in the Decision Records index, where they were
decided. **Diagrams** holds one row per drawing that opens on a page of its
own, five today, each a link in a new tab rather than a document in the
dialog, for the reason ADR: Every diagram opens on its own page gives; its
addendum has the list and the test that holds it to the server's catalogue.
The order from the top is now App Architecture, SQL vs Cosmos DB, Diagrams,
Hosting, CI/CD, Best Practices, Decision Records, Changelog, About, and the
browser suite reads every heading.

![The rail on the live site at 1.0.0.107: App Architecture with its four rows, then SQL vs Cosmos DB with one row, then Diagrams with five link rows, each with the new-tab icon](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sidebar-sections.png)

![The comparison open from its section: the record dialog over the inventory, the rail row marked current, the page's first paragraphs](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/sidebar-sql-vs-cosmos-open.png)


## Addendum, 2026-09-15: every section is closed on arrival, shipped as 1.0.0.135

The records index has been a closed `details` since it was written, and the other ten sections were
always open. That was the right call at four sections and the wrong one at eleven: the rail arrived
holding about a hundred rows, the reader's own section was somewhere inside them, and finding
anything meant scrolling past everything.

**Every section is a `details` now, and every one of them starts closed.** The sidebar arrives as
eleven headings, which is what a table of contents is for, and a section opens on a click. The state
is deliberately not remembered between visits: the reason to collapse is the arrival, not the
session, and a reader who wants one section open wants it open once.

Three things stay as they were. The element does the work, so the keyboard path, the screen reader
announcement and the disclosure marker all come from `details` and `summary` rather than from a
reimplementation with a button and a piece of state. The icons-only rail keeps its rows, because it
shows no headings to collapse and a closed section there would be a triangle with nothing to read.
And the current row still reads as current while its document is open.

The browser suite carries the rule: eleven sections, none open on arrival, one open after a click,
and none open again after a reload. Every spec that opens a document now names the section it lives
in, through one helper in `tests/e2e/app.ts`, which is the part of this change that touched ten
files.

```live path=src/components/SideNav.tsx region=section-shell
```


## Addendum, 2026-09-21: a fourteenth section, for the author, shipped as 1.0.0.171

Everything in the sidebar was about the site, and nothing was about the person who built it. The
reader this site is for is a recruiter or a hiring manager deciding whether to start a
conversation, and a conversation is with somebody. So the last section, right under About, is
**Author**, with one page in it, **About Steven**: who he is at work in three sentences, three ways
to reach him, the rest of his life in small headed blocks, a closing line. The intro strip over the
inventory gained a fourth link, "Who built this", which opens it.

**The words are his.** They live in `docs/AUTHOR.md`, a served document like every other, so the
catalogue test, the page sweep and the house voice read it. He edits a draft and approves the
text. 1.0.0.171 carries the draft, shipped at his word so that he could review it on the live
page, and his edits follow it. Nothing about him or his family reaches the page any other way: an
image the document names is shown only if it is one of the photographs in
`src/lib/authorPhotos.json`, and any other is left out by the layout.

**It is a page and not a letter, and the document stays a document.** A panel with buttons, blocks
two to a row and photographs side by side are not things markdown can say. A component with the
words typed into it would take the words out of the documents, where the tests read them and where
he edits them. So the layout is a function, `src/lib/author.ts`, over the HTML the one renderer
already wrote: second-level headings open the panels, third-level headings open the blocks, a list
in which every item is one link is a row of buttons, a rule opens the closing panel. It arrives on
demand with the renderer, so the inventory's first load pays nothing for it. The dialog is wider for
this one page and stays solid, as every document's is.

```live path=src/lib/author.ts region=panels
```

**What is deliberately not on it, and why.** The page grew from an introduction he once wrote to a
new team, which was private and is dated. Left out because they go stale: an age, and anything that
was true on one date. Left out because they are somebody else's: the names of private people other
than the one he approved, and anybody from a former employer. Left out because they place his home:
a street, a neighbourhood, a phone number, any email address. There is no contact form and no
address to harvest: the resume, LinkedIn and GitHub are the three ways to reach him. `AuthorPageTests`
holds all of it, each rule with its reason. The few exact words that must never appear are held as
salted digests and not as text, because the test is as public as the page, and a list of what to
keep off the page, written out, would publish it.

**The photographs.** Served the way the inventory's are (ADR: Responsive photos): several widths of
one cut, WebP before JPEG, the real width of every file in the `srcset`, the box reserved so
nothing jumps, lazy below the fold, from `/api/images/author` with the same day of caching.
`scripts/author_photos.mjs` cuts them from originals that stay outside the repository, refuses to
scale one up, and reads every file back to prove it holds no EXIF, XMP, IPTC or colour profile: a
phone's photograph knows where it was taken, and this page must not. The same test opens every
file in the gate, checks its real width against the list, and fails on any file in the folder that
the list does not name. The floor for a photograph's largest cut is 1920 pixels. The two snapshots
of the rabbits are 1600, and the list says why: they are cropped tight so the room is not the
subject, and 1600 is every pixel the crop has.

**The frame and the blocks.** Every photograph wears one frame, from two tokens,
`--frame-photo-border` and `--frame-photo-shadow`: a 3 pixel deep teal border, a gold hairline
outside it, 8 pixel corners, a soft teal shadow. All gold on the pictures was tried and was too
much. The gold went to the headed blocks instead: each has a 3 pixel top edge, gold then teal then
gold, by the block's place on the page. No block carries a colour class, so an edit to the
document cannot leave two neighbours the same. Gold there is trim on a white card with no amber
near it, which is the job the style page gives it; the panels keep the dark green left edge and the
dark green rule with its gold tick.

```live path=src/components/DocsMenu.module.css region=author-alternation
```

The browser suite opens the page from the rail and from the phone's drawer, counts the three
buttons and their 44 pixels, loads every photograph from this site in its frame, reads the blocks'
top edges for the alternation, and holds that nothing on the page is wider than a phone. The
suite's count of headings is fourteen.

1.0.0.172 added the two photographs of him and Katie that were in the approved mock-up, with his
yes to publishing them and the photographer's credit under the panels; a phone is never offered a
file of any photograph wider than 960.

Files this addendum decided about:
[`docs/AUTHOR.md`](https://github.com/SteveStout/TheYard/blob/main/docs/AUTHOR.md),
[`src/lib/author.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/author.ts),
[`src/lib/authorPhotos.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/authorPhotos.ts) and
[`src/lib/authorPhotos.json`](https://github.com/SteveStout/TheYard/blob/main/src/lib/authorPhotos.json),
[`scripts/author_photos.mjs`](https://github.com/SteveStout/TheYard/blob/main/scripts/author_photos.mjs),
[`api/TheYard.Tests/AuthorPageTests.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/AuthorPageTests.cs) and
[`tests/e2e/author.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/author.spec.ts).
