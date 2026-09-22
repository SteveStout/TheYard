import { describe, expect, it } from 'vitest';
import component from '../components/Ribbons.tsx?raw';
import sheet from '../components/Ribbons.module.css?raw';
import data from './ribbons.ts?raw';
import { FLARES, RIBBONS, SHINE, SPARKS } from './ribbons';

/**
 * The ribbon ground's performance rules (ADR: The glass look, the addendum on
 * the ribbon ground), held against the files themselves so a later edit cannot
 * quietly make the page's background the most expensive thing on it.
 */

/** Every rule in the sheet as [selector, body], keyframes and media wrappers left out. */
function rules(css: string): [string, string][] {
  const flat = css.replace(/\/\*[\s\S]*?\*\//g, '');
  return Array.from(flat.matchAll(/([^{}@]+)\{([^{}]*)\}/g), (match) => [
    match[1].trim(),
    match[2],
  ]);
}

describe('the ribbon ground', () => {
  it('is the approved drawing, whole', () => {
    expect(RIBBONS).toHaveLength(13);
    expect(SHINE).toHaveLength(3);
    expect(FLARES).toHaveLength(2);
    expect(SPARKS).toHaveLength(70);
  });

  it('adds under 12 kB of markup, data and style', () => {
    const bytes = new TextEncoder().encode(component + sheet + data).length;
    expect(bytes).toBeLessThan(12_000);
  });

  it('does not move: no animation, no transition, no keyframes, painted once', () => {
    for (const [selector, body] of rules(sheet)) {
      expect(body, selector).not.toMatch(/animation|transition|will-change/);
    }
    expect(sheet).not.toMatch(/@keyframes/);
    expect(component).not.toMatch(/<animate|requestAnimationFrame|setInterval/);
  });

  it('is one drawing, centred by the stylesheet alone', () => {
    expect(component.match(/<svg/g)).toHaveLength(1);
    expect(sheet).toMatch(/\[data-rail='open'\]\) \.drawing \{[^}]*left: var\(--rail-width\)/);
  });

  it('is as tall as the large viewport, so the address bar on a phone cannot rescale it', () => {
    const layer = rules(sheet).find(([selector]) => selector === '.layer')?.[1] ?? '';
    expect(layer).toMatch(/height:\s*100lvh/);
    expect(layer).not.toMatch(/inset:\s*0/);
    expect(layer).toMatch(/contain:\s*strict/);
  });

  it('fetches nothing: every url in it is one of its own gradients or filters', () => {
    for (const url of (component + sheet).matchAll(/url\(([^)]*)\)/g)) {
      expect(url[1]).toMatch(/^#ribbon-/);
    }
    expect(component + sheet).not.toMatch(/<image|https?:/);
  });
});
