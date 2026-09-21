/**
 * The swatches on the Colour and style page, drawn from the token sheet and
 * never from a picture (ADR-016, the addendum on the style section). A picture
 * of swatches is wrong the day a token changes. A `swatches` fence in a served
 * document lists tokens, one a line with the name a person calls it, and this
 * turns the fence into a sheet: each token as a chip painted with the token
 * ITSELF, `var(--token)`, so the chip is whatever the page is running on, with
 * the value the sheet states beside it and its measured contrast on white and
 * on the page grey. No React in here, and nothing a fence writes reaches the
 * page unescaped.
 */
import { contrast, hexTokens } from './contrast';

export type SwatchLine = { token: string; label: string };

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** A fence's lines: `--token | what a person calls it`. Anything else on a line is dropped. */
export function swatchLines(fence: string): SwatchLine[] {
  return fence
    .split('\n')
    .map((line) => line.match(/^\s*(--[a-z0-9-]+)\s*(?:\|\s*(.*?))?\s*$/))
    .filter((match): match is RegExpMatchArray => match !== null)
    .map((match) => ({ token: match[1], label: match[2] ?? '' }));
}

/**
 * The sheet, as HTML. `sheet` is the text of tokens.css. A token the sheet
 * does not define is drawn as a gap that says so, because a swatch page that
 * quietly skips a token is the stale picture again.
 */
export function swatchSheet(fence: string, sheet: string): string {
  const values = new Map(hexTokens(sheet).map((entry) => [entry.name, entry.hex]));
  const white = values.get('--color-surface');
  const grey = values.get('--color-bg');
  const measured = (hex: string) =>
    white === undefined || grey === undefined
      ? hex
      : `${hex} · ${contrast(hex, white).toFixed(2)} on white · ${contrast(hex, grey).toFixed(2)} on grey`;
  // Every gradient the sheet defines, with the way it runs: the header's top to bottom, the ribbon
  // ground's left to right (ADR: The glass look, the addendum on the ribbon ground).
  const gradients = new Map(
    Array.from(sheet.matchAll(/(--gradient-[a-z0-9-]+):\s*([^;]+);/g), (match) => [
      match[1],
      /\b90deg\b/.test(match[2]) ? 'left to right' : 'top to bottom',
    ])
  );

  const items = swatchLines(fence).map(({ token, label }) => {
    const hex = values.get(token);
    const isGradient = gradients.has(token);
    if (hex === undefined && !isGradient) {
      return `<li class="swatch swatch-missing"><span class="swatch-chip"></span><span class="swatch-label">${escape(label)}</span><code>${escape(token)}</code><span class="swatch-figures">not in the token sheet</span></li>`;
    }
    const figures = hex === undefined ? (gradients.get(token) ?? '') : measured(hex);
    return `<li class="swatch${isGradient ? ' swatch-wide' : ''}"><span class="swatch-chip" style="background: var(${escape(token)})"></span><span class="swatch-label">${escape(label)}</span><code>${escape(token)}</code><span class="swatch-figures">${figures}</span></li>`;
  });

  return `<ul class="swatches" data-testid="swatches">${items.join('')}</ul>\n`;
}
