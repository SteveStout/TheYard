# Background and ribbon

**The ground behind every page is code, not a picture. It costs no request and never animates.**

```ribbon-strip
A live strip, drawn by the same component the page uses, from the same data in src/lib/ribbons.ts. Its gradient and filter ids are its own.
```

## The ground

A gradient from green-grey on the left to white on the right, so the side rail sits on the darker end and the reading side stays bright. Where no ribbon is drawn, the page ground is the light grey. Every value is a design token: a named CSS custom property (a CSS variable) holding one design decision.

```swatches
--gradient-ground | The ground, left to right
--color-ground-left | Ground, left
--color-ground-mid | Ground, middle
--color-ground-right | Ground, right
--color-ribbon-gold | Ribbon gold
--color-ribbon-gold-soft | Ribbon gold, soft
--color-ribbon-teal | Ribbon teal
--color-ribbon-teal-light | Ribbon teal, light
--color-ribbon-green | Ribbon dark green
--color-ribbon-shine | Highlight gold
--color-ribbon-shine-pale | Highlight, pale end
--color-ribbon-shine-white | Highlight, white end
--color-ribbon-flare | Flare and spark centre
--color-ribbon-spark | Spark gold
--color-ribbon-star | A flare's arms
```

## The ribbons

{{live:array RIBBONS}} ribbons, {{live:array SHINE}} highlight strands, {{live:array FLARES}} flares and {{live:array SPARKS}} sparks in one inline SVG, counted from [`src/lib/ribbons.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.ts) when the page opens. They start at the rail's edge and put the gold highlights up front.

```live path=src/lib/ribbons.ts region=first-ribbons
```

Each line is one ribbon: its path, its colour, its width and its strength.

## The watermark

Dotted rows only, in the chart teal at a tenth of its strength. The lightning mark left the watermark in 1.0.3.28 because it read as a pale box on a desk. The bolt stays in the header.

## Why it stands still

Moving, the front page spent 6.9 s of main-thread work in every 20 s; still, 0.6 s. Measured in Chrome with its GPU over a minute of the front page ([ADR-081](https://theyard.stevenstout.biz/?doc=adr-glass-look), the addendum "the ribbons stand still"). The drawing costs one paint and then nothing. Nothing in it animates, and a test says so:

```live path=src/lib/ribbons.test.ts region=does-not-move
```

The ground is part of the page, not the window. It scrolls with the words in front of it, like a printed sheet, so the page never slides over a still picture. The ribbons sit in the first screen of every page and the gradient ground carries on below them. [`Ribbons.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Ribbons.module.css) holds the layer, and [`ribbons.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.test.ts) and the browser suite hold that it is pinned to the page:

```live path=src/components/layout/Background/Ribbons.module.css region=ground-layer
```

```live path=src/lib/ribbons.test.ts region=part-of-the-page
```

## Glass over the ribbons

The worst case for a word is the thinnest glass over the darkest thing behind it. Through a panel over the watermark at its worst, the one grey every secondary word uses reads 6.53, and [`StyleRulesTests`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Tests/StyleRulesTests.cs) computes that figure from the design tokens and holds the faintest text to 4.5 on it. When a reader's system asks for reduced transparency the glass turns solid; under forced colours the site steps aside and the watermark is not drawn.

## The Chrome bug

Two copies of the drawing shared gradient names, and Chrome painted the page's copy blank. Each copy now names its own ids, and [`coverage.spec`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/coverage.spec.ts) holds every id to one use per page. The strip at the top of this page is a second copy, and it is the proof.

```live path=src/lib/ribbons.test.ts region=own-ids
```

## Files

- [`src/components/layout/Background/Ribbons.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Ribbons.tsx)
- [`src/lib/ribbons.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.ts)
- [`src/components/layout/Background/Ribbons.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Ribbons.module.css)
- [`src/components/layout/Background/Watermark.tsx`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Watermark.tsx)
- [`src/components/layout/Background/Watermark.module.css`](https://github.com/SteveStout/TheYard/blob/main/src/components/layout/Background/Watermark.module.css)
- [`src/lib/ribbons.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/ribbons.test.ts)
- [`tests/e2e/glass.spec.ts`](https://github.com/SteveStout/TheYard/blob/main/tests/e2e/glass.spec.ts)
