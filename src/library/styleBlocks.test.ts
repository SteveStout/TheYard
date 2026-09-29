import { describe, expect, it } from 'vitest';
import { glossarySheet, readoutSheet, ribbonStrip, tileSheet } from './styleBlocks';
import { withHeadingIds } from '../lib/markdown';

describe('the Style section blocks', () => {
  it('draws a tile per line, linked to its page, with the icon left for the site map to draw', () => {
    const html = tileSheet(
      'color-style | Colour and style | Every colour. | style\nui-architecture | UI | How. | architecture'
    );
    expect(html.match(/<li>/g)).toHaveLength(2);
    expect(html).toContain('href="/?doc=color-style"');
    expect(html).toContain('data-doc="ui-architecture"');
    expect(html).toContain('data-glyph="style"');
    expect(html).toContain('op-glass op-tile');
  });

  it('draws a readout per line with the value the API wrote into the fence', () => {
    const html = readoutSheet('Style tests | 13\nDesign tokens | 201');
    expect(html.match(/data-testid="style-readout"/g)).toHaveLength(2);
    expect(html).toContain('<dd data-testid="style-readout">201</dd>');
  });

  it('draws a glossary entry that links to the section it is used in, and escapes what it is given', () => {
    const html = glossarySheet(
      'Inline SVG | <svg> | Code that draws. | background-ribbon#the-ribbons | Background and ribbon'
    );
    expect(html).toContain('<code>&lt;svg&gt;</code>');
    expect(html).toContain('href="/?doc=background-ribbon#the-ribbons"');
    expect(html).toContain('data-anchor="the-ribbons"');
    expect(html).toContain('See Background and ribbon');
  });

  it('leaves a box for the live ribbons, with its caption', () => {
    const html = ribbonStrip('A live strip.');
    expect(html).toContain('data-ribbon-strip');
    expect(html).toContain('<figcaption>A live strip.</figcaption>');
  });

  it('gives every second-level heading an id from its words, told apart by a number when two match', () => {
    const html = withHeadingIds(
      '<h2>The glass</h2><p>x</p><h2>Status colours, reserved</h2><h2>The glass</h2>'
    );
    expect(html).toContain('<h2 id="the-glass">The glass</h2>');
    expect(html).toContain('<h2 id="status-colours-reserved">');
    expect(html).toContain('<h2 id="the-glass-1">');
  });
});
