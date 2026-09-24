import { describe, expect, it } from 'vitest';
import tokens from '../styles/tokens.css?raw';
import { ICON } from './icons';

// Every component's source, and the shell's, read as text by the bundler: this
// project carries no Node types on purpose (vite.config.ts), so the files come
// in the way tokens.css does above rather than through node:fs. App.tsx is
// in the list because it draws the header's hamburger itself (1.0.3.10: the
// sweep read its stroke at 2 while every icon was on the token, and this test
// had not been looking at the file).
const components = import.meta.glob(['../components/*.tsx', '../App.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

// The component sheets, for the strokes a sheet writes rather than a component.
const sheets = import.meta.glob('../components/*.module.css', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** A drawing is not an icon: the fallback car in VehicleImage is a picture at its own size. */
const DRAWINGS = ['VehicleImage.tsx'];

/**
 * The strokes a sheet may write in a number of its own, by selector, each a
 * drawing rather than an icon (1.0.3.10): a chart's axis, grid, line and
 * readout, the health rings and the sparkline on the Admin tab, the pass and
 * fail marks on its tests card (a filled disc with a bold glyph, drawn at 16
 * and 44 px from one 24-unit viewBox, so its strokes scale with the mark and
 * the icon stroke would read as a hairline at the small size), and the
 * watermark. Anything else a sheet strokes is on --icon-stroke.
 */
const DRAWN_IN_CSS: Record<string, string[]> = {
  'AdminPanel.module.css': [
    '.axis',
    '.axisUnit',
    '.gridLine',
    '.readoutRule',
    '.readoutBox',
    '.line',
    '.ringTrack',
    '.ringHeld',
    '.spark',
    '.resultMark path',
    '.resultDisc',
    // The activity chart's stacked bands (1.0.3.12): the two-pixel gap in the card's ground
    // between bands, and the halo round a band's name. Drawings, not icons.
    '.bandEdge',
    '.bandLabel',
    // The crosshair (1.0.3.14): a hairline down the day and a ringed dot on each band.
    '.crosshair line',
    '.crossDot',
  ],
  'Watermark.module.css': ['.rings', '.rows'],
};

describe('the icons', () => {
  it('are the sizes and the stroke the token sheet names', () => {
    expect(tokens).toContain(`--icon-sm: ${ICON.sm}px;`);
    expect(tokens).toContain(`--icon-md: ${ICON.md}px;`);
    expect(tokens).toContain(`--icon-stroke: ${ICON.stroke};`);
  });

  it('are never drawn at a number a component wrote itself', () => {
    const written: string[] = [];
    for (const [path, source] of Object.entries(components)) {
      const file = path.split('/').pop() ?? path;
      if (DRAWINGS.includes(file)) continue;
      for (const match of source.matchAll(/<svg[^>]*\swidth="(\d+)"/g)) {
        written.push(`${file}: width="${match[1]}"`);
      }
      for (const match of source.matchAll(/strokeWidth="([\d.]+)"/g)) {
        written.push(`${file}: strokeWidth="${match[1]}"`);
      }
    }
    expect(Object.keys(components).length).toBeGreaterThan(10);
    expect(Object.keys(components).some((path) => path.endsWith('/App.tsx'))).toBe(true);
    expect(written).toEqual([]);
  });

  it('are never stroked at a number a sheet wrote itself, unless the rule is a declared drawing', () => {
    const written: string[] = [];
    for (const [path, source] of Object.entries(sheets)) {
      const file = path.split('/').pop() ?? path;
      const declared = DRAWN_IN_CSS[file] ?? [];
      for (const rule of source.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        const stroke = /stroke-width\s*:\s*([^;]+);/.exec(rule[2]);
        if (!stroke) continue;
        if (stroke[1].trim() === 'var(--icon-stroke)') continue;
        const selector =
          rule[1]
            .split('*/')
            .pop()
            ?.trim()
            .split(/\s*,\s*/)
            .join(', ') ?? '';
        if (declared.includes(selector)) continue;
        written.push(`${file} ${selector}: stroke-width ${stroke[1].trim()}`);
      }
    }
    expect(Object.keys(sheets).length).toBeGreaterThan(10);
    expect(written).toEqual([]);
  });

  it('declares only drawings that exist', () => {
    for (const [file, selectors] of Object.entries(DRAWN_IN_CSS)) {
      const source = Object.entries(sheets).find(([path]) => path.endsWith('/' + file))?.[1] ?? '';
      expect(source, file).not.toBe('');
      for (const selector of selectors) {
        expect(source, `${file} ${selector}`).toContain(`${selector} {`);
      }
    }
  });
});
