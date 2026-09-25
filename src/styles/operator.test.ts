import { describe, expect, it } from 'vitest';
import operator from './operator.css?raw';
import tokens from './tokens.css?raw';

/**
 * The operator's look (ADR: The glass look, the addendum on the operator's
 * look) is tokens and one shared sheet. This holds the sheet to the tokens and
 * the tokens to the palette: no new colour, one rule, one bracket, one radius.
 */
const region = (() => {
  const start = tokens.indexOf('/* #region operator-look */');
  const end = tokens.indexOf('/* #endregion operator-look */');
  if (start < 0 || end < 0) throw new Error('tokens.css has no operator-look region');
  return tokens.slice(start, end);
})();

// A rule by its own selector exactly, not as one half of a pair: the sheet read
// as rules, each selector with its comments taken off.
const rule = (selector: string) => {
  for (const [, head, body] of operator.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    if (head.replace(/\/\*[\s\S]*?\*\//g, '').trim() === selector) return body;
  }
  throw new Error(`operator.css has no rule ${selector}`);
};

describe("the operator's look", () => {
  it('adds no colour: every value in its region is a token or a length', () => {
    expect(region).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/);
    expect(region).toContain('--rule-panel-color: var(--color-teal-deep);');
    expect(region).toContain('--rule-tile-color: var(--color-gold-light);');
    // The ring at 100 per cent (the tweaks pass, A3): a faint track under a dark fill, a gold marker.
    expect(region).toContain('--ring-track: var(--color-mark-track);');
    expect(region).toContain('--ring-first: var(--color-teal-deep);');
    expect(region).toContain('--ring-second: var(--color-gold-light);');
    expect(region).toContain('--ring-marker: var(--color-mark-marker);');
  });

  it('draws a panel as glass with the dark green rule, and a tile with the gold one', () => {
    expect(rule('.op-glass')).toContain('background: var(--glass-bg);');
    expect(rule('.op-glass')).toContain('border-top: var(--rule-panel);');
    expect(rule('.op-glass')).toContain('border-radius: var(--radius-glass);');
    expect(rule('.op-tile')).toContain('border-top: var(--rule-tile);');
    expect(rule('.op-tile')).toContain('--op-bracket-color: var(--rule-tile-color);');
  });

  it('bends each bracket round the panel radius, an arc and never an L, at the one stroke', () => {
    expect(rule('.op-glass::before')).toContain('border-top-left-radius: var(--radius-glass);');
    expect(rule('.op-glass::after')).toContain('border-bottom-right-radius: var(--radius-glass);');
    expect(rule('.op-glass::before')).toContain('var(--bracket-stroke)');
    expect(rule('.op-glass::after')).toContain('var(--bracket-stroke)');
    // The sizes come from the tokens: a card, a rail and a tile; the sheet writes no size of its own.
    // A media query's breakpoint is not a size (the phone's thicker glass).
    expect(operator).not.toMatch(/(?<![-\w])(width|height):\s*\d+px/);
    for (const size of [
      '--bracket-size: 18px;',
      '--bracket-size-tile: 10px;',
      '--bracket-size-rail: 14px;',
      '--bracket-stroke: 2px;',
    ]) {
      expect(region).toContain(size);
    }
  });

  it('keeps every pill in a group round, the chosen one filled with the accent', () => {
    expect(rule('.op-seg')).toContain('border-radius: var(--radius-full);');
    expect(operator).toContain(".op-seg > [aria-pressed='true']");
    expect(operator).toMatch(
      /\.op-seg > \[aria-current='page'\] \{\s*background: var\(--color-accent\);\s*color: var\(--color-on-accent\);/
    );
  });

  // The tweaks pass (B3): a 24 px hairline grid inside a glass panel, multiplied into
  // its fill, and never on a tile, a panel read on white, a dialog's own sheet or
  // one of the inventory's hundred cards.
  it('rules a glass panel with a hairline grid, and a tile, a solid panel, a dialog sheet and a card without one', () => {
    const grid = rule('.op-glass:not(.op-tile, .op-solid, .op-sheet, .op-card)');
    expect(grid).toContain('var(--glass-grid-line) 1px, transparent 1px');
    expect(grid).toContain('background-size: var(--glass-grid-size) var(--glass-grid-size);');
    expect(grid).toContain('background-blend-mode: multiply;');
    expect(tokens).toContain(
      '--glass-grid-line: color-mix(in srgb, var(--color-teal-deep) 3.5%, transparent);'
    );
    expect(tokens).toContain('--glass-grid-size: 24px;');
    expect(rule('.op-tile')).not.toContain('background-image');
    // The document dialog's clear sheet (B1b) is the shared glass's too.
    expect(rule('.op-glass.op-sheet')).toContain('background: var(--dialog-sheet-bg);');
    expect(rule('.op-glass.op-sheet')).toContain('backdrop-filter: var(--dialog-sheet-filter);');
    // And frosted on a phone (1.0.3.24), where the clear sheet read as broken.
    expect(operator).toMatch(
      /@media \(max-width: 639\.98px\) \{\s*\.op-glass\.op-sheet \{\s*background: var\(--dialog-page-bg\);/
    );
  });
});
