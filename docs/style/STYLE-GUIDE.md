# Style guide

**A style guide the build enforces: {{live:design-tokens}} design tokens, {{live:facts api/TheYard.Tests/StyleRulesTests.cs}} style tests, and no page ships off-brand.**

Teal fills, dark green draws, gold trims, frosted glass over a light ground, and every word, number and photo fully solid.

Every colour, size and width is a design token: a named CSS custom property (a CSS variable) holding one design decision. This section explains how the site looks and why, and the build enforces every rule on these pages, so a change that breaks one does not ship.

## In plain words

This section explains how the site looks and why. Every colour and size is a named value declared once (a design token), and the build stops a change that breaks a rule here from shipping.

What that is worth: a developer changes a colour in one place and every page follows, and the organization ships no page off-brand because the build checks every one.

## In this section

```tiles
color-style | Colour and style | Every colour, size and width as a design token, with live swatches and contrast. | style
background-ribbon | Background and ribbon | The teal and gold ground, drawn in code and never moving. | ai
ui-architecture | UI architecture | How the look is built, file by file, one job per file, and the tests that hold it. | architecture
document-style | How the documents are styled | Plain Markdown drawn as glass panels, with code read from the running build. | records
```

## The standing rules

- One font everywhere: IBM Plex Sans ([`src/styles/typography.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/typography.css), held by [StyleRulesTests](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs)).
- Rounded corners, never square ([`src/styles/sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css), the radius design tokens).
- Panels are frosted glass over the ribbon ground ([`src/styles/panels.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/panels.css), `.op-glass`).
- Words, numbers and photos at full strength ([`tests/e2e/glass.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.spec.ts)).
- The header and the ground keep their colours ([`src/styles/colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css), `--gradient-header`, `--gradient-ground`).
- Every page styled the same, no page missed ([`tests/e2e/coverage.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/coverage.spec.ts), the page sweep).

## Glossary of styling terms

The words these pages use, in plain language, each linked to the section that shows it in use.

```glossary
Design token |  | A named CSS custom property holding one design decision, such as the accent colour or a corner radius. The industry term, set by the W3C Design Tokens Community Group. | color-style#brand-colours | Colour and style
CSS custom property | --name | The CSS feature a design token is written in, often called a CSS variable. Declared once, read anywhere with var(). | color-style#how-the-sheet-is-built | Colour and style
Contrast ratio |  | How far apart a text colour and its background are, from 1 to 21. WCAG AA asks 4.5 for body text and 3.0 for large text and marks. | color-style#grounds-and-text | Colour and style
Frosted glass | backdrop-filter | A see-through panel that blurs whatever sits behind it. The panel is tinted, the words on it stay solid. | color-style#the-glass | Colour and style
Gradient | linear-gradient() | A colour that blends into another across a box. The ground runs green-grey to white, the header dark green to teal. | background-ribbon#the-ground | Background and ribbon
Inline SVG | <svg> | A drawing written as code inside the page, so it needs no image download and stays sharp at any size. | background-ribbon#the-ribbons | Background and ribbon
Breakpoint | @media | A screen width where the layout changes, such as the side rail docking at 1024 pixels. | color-style#sizes-and-widths | Colour and style
Container query | @container | A layout rule that reacts to the width of its own box rather than the whole screen. The landing tiles and the inventory grid use one. | ui-architecture#the-layers | UI architecture
CSS module | *.module.css | A stylesheet scoped to one component, so its class names cannot leak into another. | ui-architecture#the-layers | UI architecture
Colour mix | color-mix() | Blends two colours in the browser, used here for see-through tints so a colour is written only once. | color-style#chart-colours | Colour and style
Reduced transparency | prefers-reduced-transparency | A reader's system setting asking for solid surfaces. The glass turns solid when it is on. | background-ribbon#glass-over-the-ribbons | Background and ribbon
Forced colours | forced-colors | A high-contrast mode where the system picks every colour. The site steps aside and lets it. | background-ribbon#glass-over-the-ribbons | Background and ribbon
```

## How it is enforced

```readouts
Style tests | {{live:facts api/TheYard.Tests/StyleRulesTests.cs}}
Design tokens | {{live:design-tokens}}
Design token files | {{live:design-token-files}}
Documents swept | {{live:documents}}
```

Numbers read from the build at request time, the same way the swatches are: [`colors.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/colors.css) {{live:design-tokens src/styles/colors.css}}, [`sizes.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/sizes.css) {{live:design-tokens src/styles/sizes.css}}, [`typography.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/typography.css) {{live:design-tokens src/styles/typography.css}}, [`effects.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/effects.css) {{live:design-tokens src/styles/effects.css}}; [StyleRulesTests](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs) {{live:facts api/TheYard.Tests/StyleRulesTests.cs}} facts. The page sweep reads every document at a phone and a desk width.

The facts, by name, as the test file declares them:

```text
{{live:fact-names api/TheYard.Tests/StyleRulesTests.cs}}
```

## The decisions behind it

- [ADR-016, The palette](https://theyard.stevenstout.biz/?doc=adr-palette)
- [ADR-081, The glass look](https://theyard.stevenstout.biz/?doc=adr-glass-look)
- [ADR-083, The tweaks pass](https://theyard.stevenstout.biz/?doc=adr-tweaks)
- [ADR-019, The React configuration, explained](https://theyard.stevenstout.biz/?doc=adr-react)
