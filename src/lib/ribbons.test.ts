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

const animated = rules(sheet).filter(
  ([selector, body]) => /animation:(?!\s*none)/.test(body) && !/^(from|to|\d)/.test(selector)
);

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

  it('moves the whole drawing on a transform, and nothing inside it moves on anything but opacity', () => {
    expect(animated.map(([selector]) => selector).sort()).toEqual(
      ['.drawing', '.flareGroup', '.sparks circle'].sort()
    );
    expect(rules(sheet).find(([selector]) => selector === '.drawing')?.[1]).toMatch(
      /will-change:\s*transform/
    );
    const frames = Array.from(sheet.matchAll(/@keyframes\s+(\w+)\s*\{([\s\S]*?\}\s*)\}/g));
    for (const [, name, body] of frames) {
      if (name === 'drift') expect(body).toMatch(/transform/);
      else expect(body).not.toMatch(/transform/);
    }
  });

  it('puts no blur on anything that moves: the sparks glow through their gradient', () => {
    for (const [selector, body] of animated) {
      expect(body, selector).not.toMatch(/filter/);
    }
    expect(component).not.toMatch(/className=\{styles\.flareGroup\}[^>]*filter=/);
    expect(component).toMatch(/fill="url\(#ribbon-spark\)"/);
    const sparks = component.slice(component.indexOf('SPARKS.map'));
    expect(sparks.slice(0, sparks.indexOf('))}'))).not.toMatch(/filter/);
  });

  it('stops every motion when the visitor asks for less', () => {
    const reduced = sheet.slice(sheet.indexOf('prefers-reduced-motion'));
    const block = reduced.slice(
      0,
      reduced.indexOf('@media', 10) > 0 ? reduced.indexOf('@media', 10) : undefined
    );
    for (const [selector] of animated) {
      expect(block, selector).toContain(selector);
    }
    expect(block).toMatch(/animation:\s*none/);
  });

  it('fetches nothing: every url in it is one of its own gradients or filters', () => {
    for (const url of (component + sheet).matchAll(/url\(([^)]*)\)/g)) {
      expect(url[1]).toMatch(/^#ribbon-/);
    }
    expect(component + sheet).not.toMatch(/<image|https?:/);
  });
});
