import { describe, expect, it } from 'vitest';
import { swatchLines, swatchSheet } from './swatches';

const sheet = `:root {
  --color-bg: #e9e6e7;
  --color-surface: #ffffff;
  --color-accent: #006360; /* teal */
  --gradient-header: linear-gradient(180deg, var(--color-green-dark), var(--color-teal-header));
}`;

describe('the swatches on the Colour and style page', () => {
  it('reads a fence a line at a time: a token, and what a person calls it', () => {
    expect(swatchLines('--color-accent | Teal, the accent\n\nnot a token\n--color-bg')).toEqual([
      { token: '--color-accent', label: 'Teal, the accent' },
      { token: '--color-bg', label: '' },
    ]);
  });

  it('paints a chip with the token itself, and states the value and the measured contrast', () => {
    const html = swatchSheet('--color-accent | Teal', sheet);
    expect(html).toContain('style="background: var(--color-accent)"');
    expect(html).toContain('#006360 · 7.11 on white · 5.73 on grey');
    expect(html).toContain('<code>--color-accent</code>');
  });

  it('draws the one gradient as a wide sample, with no figure a gradient does not have', () => {
    const html = swatchSheet('--gradient-header | The header', sheet);
    expect(html).toContain('swatch-wide');
    expect(html).toContain('var(--gradient-header)');
    expect(html).toContain('top to bottom');
  });

  it('draws every gradient the sheet defines, and says which way each one runs', () => {
    const two = `${sheet}\n:root { --gradient-ground: linear-gradient(90deg, #dcebe7 0%, #ffffff 100%); }`;
    const html = swatchSheet('--gradient-ground | The ground\n--gradient-header | The header', two);
    expect(html).not.toContain('not in the token sheet');
    expect(html.match(/swatch-wide/g)).toHaveLength(2);
    expect(html).toContain('left to right');
    expect(html).toContain('top to bottom');
  });

  it('says so when a token is not in the sheet, rather than skipping it', () => {
    expect(swatchSheet('--color-gone | Left', sheet)).toContain('not in the token sheet');
  });

  it('lets nothing a fence writes reach the page as markup', () => {
    const html = swatchSheet('--color-accent | <img src=x onerror=alert(1)>', sheet);
    expect(html).not.toContain('<img');
    expect(html).toContain('&lt;img');
  });
});
