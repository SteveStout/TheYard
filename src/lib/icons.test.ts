import { describe, expect, it } from 'vitest';
import tokens from '../styles/tokens.css?raw';
import { ICON } from './icons';

// Every component's source, read as text by the bundler: this project carries
// no Node types on purpose (vite.config.ts), so the files come in the way
// tokens.css does above rather than through node:fs.
const components = import.meta.glob('../components/*.tsx', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** A drawing is not an icon: the fallback car in VehicleImage is a picture at its own size. */
const DRAWINGS = ['VehicleImage.tsx'];

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
    expect(written).toEqual([]);
  });
});
