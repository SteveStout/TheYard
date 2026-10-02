# UI architecture

**How the look is built: four design token files at the bottom, one site map at the top, and a test beside every layer.** A design token is a named CSS custom property (a CSS variable) holding one design decision.

[![TheYard's UI, layer by layer: the four design token files at the bottom; base.css, panels.css and the width steps above them; then the component sheets, the site map and the document layout, each reading only the layers below it](https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/ui-architecture.svg)](https://theyard.stevenstout.biz/api/docs/diagrams/ui-architecture)

*[Open the UI architecture diagram in a new page](https://theyard.stevenstout.biz/api/docs/diagrams/ui-architecture). It reads down, never up.*

## In plain words

The look is built in layers: a few files of named colours and sizes at the bottom, shared styles above them, then one small style file per component, and one site map that feeds the menus. Each layer uses only the ones below it, and each has a test.

What that is worth: a developer changes a colour in one place and it changes everywhere, and the organization can restyle the whole site without touching every screen.

## The layers

| Layer | Where it lives |
| --- | --- |
| Design tokens | [`colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css), [`sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css), [`typography.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/typography.css), [`effects.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/effects.css); three tiers in `colors.css` |
| Base and panels | [`base.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/base.css), [`panels.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/panels.css), the width steps in [`breakpoints.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/breakpoints.ts) |
| Component modules | One CSS module beside each component under [`src/components`](https://github.com/SteveStout/TheYard/tree/main/src/components), design tokens only, one job each. The main column, the inventory grid and the landing tiles size themselves with a container query, by their own box rather than the window. |
| Shell | [`src/app`](https://github.com/SteveStout/TheYard/tree/main/src/app): the frame, the header, the footer; [`Ribbons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Ribbons.tsx) and [`Watermark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Watermark.tsx) mounted once |

## One site map

[`src/lib/siteMap.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMap.ts) feeds both the side rail and the landing page, so a section added there shows up in both places. What each section holds lives in [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts); the documents themselves in [`records.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/records.ts) and [`pages.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/pages.ts).

```live path=src/lib/siteMap.ts region=look-row
```

```live path=src/library/sections.ts region=look-menu
```

## Every document in panels

[`src/lib/docLayout.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/docLayout.ts) turns each second-level heading into a glass panel, and [`src/library/DocDialog.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocDialog.tsx) is the window it opens in. That is why this page looks like every other page.

```live path=src/lib/docLayout.ts region=layout-document
```

## Where a change goes

| The change | The file | The test that holds it |
| --- | --- | --- |
| A colour | [`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css) | [`StyleRulesTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs), [`colors.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.test.ts) |
| A panel's look | [`src/styles/panels.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/panels.css) | [`panels.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/panels.test.ts), [`glass.spec`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.spec.ts) |
| The ground | [`src/lib/ribbons.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.ts) | [`ribbons.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.test.ts) |
| A section or tile | [`src/lib/siteMap.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMap.ts), [`src/library/sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts) | [`siteMap.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/siteMap.test.ts), [`DocumentationCatalogTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DocumentationCatalogTests.cs) |
| A document | [`src/library/pages.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/pages.ts) or [`records.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/records.ts) | [`DocumentationCatalogTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/DocumentationCatalogTests.cs), [the page sweep](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/coverage.spec.ts) |
| A width step | [`src/lib/breakpoints.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/breakpoints.ts) | [`coverage.spec`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/coverage.spec.ts), [`StyleRulesTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs) |
| The frame or a hook | [`src/app/`](https://github.com/SteveStout/TheYard/tree/main/src/app) | [`FileHeaderTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs), the Back and Forward specs |
| A stylesheet | the sheet beside its component, or [`src/styles`](https://github.com/SteveStout/TheYard/tree/main/src/styles) | [`FileHeaderTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs), [`StyleRulesTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs) |

## One job per file

A file grows a job at a time and never complains. So the rule is a test, not a habit.

Every file under `src/app` and `src/library`, and every stylesheet under `src`, opens with three lines before anything else: what it does, what it does not, and which files use it. It stays under 300 lines unless it is pure data. A real one, from the hook that owns the address bar:

```live path=src/app/hooks/useAddressBar.ts region=header
```

And one from a stylesheet, which carries the same three lines:

```live path=src/library/DocSwatches.module.css region=header
```

The table of contents the code split left behind: [`src/app/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.tsx) names each hook once, with what it gives back.

```live path=src/app/App.tsx region=table-of-contents
```

[`FileHeaderTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/FileHeaderTests.cs) fails the build for any file without the header, checks "Used by" against the files that really import it, and holds the 300-line cap. The numbers below are read from the repository at build time.

```live path=api/TheYard.Tests/FileHeaderTests.cs region=the-header-rule
```

```text
{{live:fact-names api/TheYard.Tests/FileHeaderTests.cs}}
```

```readouts
Files with the header | {{live:headers}}
Stylesheets under src | {{live:sheets}}
Over 300 lines, named | {{live:over-300}}
```

| The file | Before | After | Jobs it held |
| --- | --- | --- | --- |
| `src/App.tsx` | 1,100 lines | [`src/app/App.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.tsx) {{live:lines src/app/App.tsx}}, eight hooks in [`src/app/hooks`](https://github.com/SteveStout/TheYard/tree/main/src/app/hooks) | Seven: the address bar, navigation, the inventory, the open vehicle, the account, the rail, the frame |
| `DocsMenu.tsx` | 1,308 lines | [`src/library`](https://github.com/SteveStout/TheYard/tree/main/src/library): [`records.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/records.ts) {{live:lines src/library/records.ts}} is data, [`sections.ts`](https://github.com/SteveStout/TheYard/blob/main/src/library/sections.ts) {{live:lines src/library/sections.ts}}, [`DocDialog.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocDialog.tsx) {{live:lines src/library/DocDialog.tsx}} | Four: the records, the pages, the sections, the document window |
| `DocDialog.module.css` | 828 lines | [`DocDialog.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocDialog.module.css) {{live:lines src/library/DocDialog.module.css}}, [`DocProse`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocProse.module.css) {{live:lines src/library/DocProse.module.css}}, [`DocSwatches`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocSwatches.module.css) {{live:lines src/library/DocSwatches.module.css}}, [`DocPanels`](https://github.com/SteveStout/TheYard/blob/main/src/library/DocPanels.module.css) {{live:lines src/library/DocPanels.module.css}}, [`AuthorPage`](https://github.com/SteveStout/TheYard/blob/main/src/library/AuthorPage.module.css) {{live:lines src/library/AuthorPage.module.css}} | Five: the window, the prose, the swatches, the reading panels, the Author page |
| `App.module.css` | 425 lines | [`App.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/app/App.module.css) {{live:lines src/app/App.module.css}}, [`InventoryView.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/app/InventoryView.module.css) {{live:lines src/app/InventoryView.module.css}} | Two: the frame, the inventory page |

The "Before" column is measured at b9ae11a (1.0.3.35) for the code and a69b292 (1.0.3.39) for the sheets; the "After" column is this build. Four component sheets are still over the line, each named in the test with its one job and the lane that will split it with its component. Records: [ADR-019](https://theyard.stevenstout.biz/?doc=adr-react), the addenda on the split and on the stylesheets; [ADR-074](https://theyard.stevenstout.biz/?doc=adr-highlighting), code that reads like code; [ADR-075](https://theyard.stevenstout.biz/?doc=adr-rules), the rule rows.

## The rest of the architecture

The API, the stores and the data live in [App Architecture](https://theyard.stevenstout.biz/?doc=architecture). This page covers only the look.
