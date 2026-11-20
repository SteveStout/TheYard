/**
 * Does:      Turns the Quality page's fences into its blocks (ADR-130, and its addendum on the page as its picture):
 *            `quality-trend` into a figure with the chart's placeholder carrying the readings as data, in the form
 *            the fence names (the first chart, a small multiple with its value now, its rule, its first reading and
 *            why it matters, or the plain figure the home page's chart came from); `quality-readouts` into a row of
 *            readouts, or, when the fence names a head, the sales pages' dark live panel on its side, the head with
 *            the LIVE tag over the readouts;
 *            `quality-tiles` into the held pill and one tile per number with its bar and note; and
 *            `quality-groups` into the rulebook's cards, each with its gauge of rules the gate checks.
 * Does not:  Read the history (the API writes every value into the fence, QualityCounts.cs and QualityReadouts.cs),
 *            draw a chart (useLiveBlocks.tsx mounts it), or hold a colour of its own (QualityBlocks.module.css).
 * Used by:   markdown.ts, qualityBlocks.test.ts.
 */
import { figure, format, parseTrend, type QualityTrendData } from '../lib/qualityTrend';

// #region quality-blocks
/** The fences this file draws, by the name on the fence. */
export const QUALITY_FENCES = [
  'quality-trend',
  'quality-readouts',
  'quality-tiles',
  'quality-groups',
] as const;
export type QualityFence = (typeof QUALITY_FENCES)[number];

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** A fence's lines, each split at its bars and trimmed, blank lines left out. */
const rows = (fence: string) =>
  fence
    .split('\n')
    .filter((line) => line.trim() !== '')
    .map((line) => line.split('|').map((cell) => cell.trim()));

/** A whole count with its comma, as the site writes one. */
const count = (value: number) => value.toLocaleString('en-US');

/** A bar of 100 units drawn to the card's width: the track, then each part from the left. */
function bar(parts: { className: string; share: number }[], kind: string): string {
  let at = 0;
  const drawn = parts
    .map(({ className, share }) => {
      const width = Math.max(0, Math.min(100 - at, share));
      const part = `<rect class="${className}" x="${at}" y="0" width="${width}" height="6" rx="3"/>`;
      at += width;
      return part;
    })
    .join('');
  const track =
    kind === 'zero'
      ? `<line class="quality-bar-held" x1="0" y1="3" x2="100" y2="3"/>`
      : `<rect class="quality-bar-track" x="0" y="0" width="100" height="6" rx="3"/>`;
  return `<svg class="quality-bar" viewBox="0 0 100 6" preserveAspectRatio="none" aria-hidden="true">${track}${drawn}</svg>`;
}

/** The figure for one `quality-trend` fence, in its form. */
function trendFigure(data: QualityTrendData): string {
  const place = `<div data-quality-trend="${escape(JSON.stringify(data))}"></div>`;
  const first = data.points[0];
  const last = data.points[data.points.length - 1];
  if (data.form === 'slip') {
    return (
      `<figure class="quality-trend" data-form="slip">` +
      `<figcaption><strong>${escape(data.label)}</strong> <span>(${escape(data.direction)})</span></figcaption>` +
      `${place}</figure>\n`
    );
  }
  if (data.form === 'small') {
    const rule = /\bfloor\b/.test(data.direction)
      ? `${data.direction}, no change may lower it`
      : `${data.direction} · ${first ? figure(first.value, data.label) : 'no reading'} on the first reading`;
    return (
      `<figure class="quality-trend" data-form="small">` +
      `<figcaption><span class="quality-name">${escape(data.label)}</span>` +
      `<strong class="quality-now">${last ? escape(figure(last.value, data.label)) : ''}</strong>` +
      `<span class="quality-rule">${escape(rule)}</span></figcaption>` +
      place +
      (data.why ? `<p class="quality-why">${escape(data.why)}</p>` : '') +
      `</figure>\n`
    );
  }
  const words =
    first && last
      ? `${format(first.value)} on ${first.date}${first.preScan ? ' (the pre-scan)' : ` at ${first.version}`}, ${format(last.value)} at ${last.version || last.date}, over ${data.points.length} readings.`
      : 'No readings in this build.';
  return (
    `<figure class="quality-trend">` +
    `<figcaption><strong>${escape(data.label)}</strong> <span>${escape(data.direction)}</span></figcaption>` +
    `${place}<p>${escape(words)}</p></figure>\n`
  );
}

/**
 * A row of readouts: each its label, its value with what it is out of, and the line under it. A fence with a head (the
 * words after its name) is the sales pages' dark live panel laid on its side (ADR: The sales pages set the site's look):
 * the head with the LIVE tag over the readouts, each value hooked as a live value; one without stays a row of tiles.
 */
function readouts(fence: string, head = ''): string {
  const cells = rows(fence).map(
    ([label = '', value = '', of = '', under = '']) =>
      `<div class="quality-readout"><span class="quality-readout-label">${escape(label)}</span>` +
      `<span class="quality-readout-value"${head ? ' data-mock="live-value"' : ''}>${escape(value)}${of ? ` <small>${escape(of)}</small>` : ''}</span>` +
      `<span class="quality-readout-under">${escape(under)}</span></div>`
  );
  if (!head) return `<div class="quality-readouts">${cells.join('')}</div>\n`;
  const top =
    `<div class="op-live-head"><span>${escape(head)}</span>` +
    `<span class="op-live-tag"><i class="op-live-dot" aria-hidden="true"></i>Live</span></div>`;
  return `<div class="quality-readouts op-live-panel" role="group" aria-label="${escape(head)}" data-mock="live-panel">${top}${cells.join('')}</div>\n`;
}

/** The held pill, then one tile per number: its name, its value now, its bar and its note. */
function tiles(fence: string): string {
  const [pill = [''], ...numbers] = rows(fence);
  const said = pill.join(' | ');
  const held = /^(\d+) of (\d+)\b/.exec(said);
  const all = held !== null && held[1] === held[2];
  const drawn = numbers.map(([name = '', value = '', kind = 'fall', share = '0', note = '']) => {
    const part = Number(share) || 0;
    const parts =
      kind === 'rise'
        ? [
            { className: 'quality-bar-was', share: part },
            { className: 'quality-bar-gain', share: 100 - part },
          ]
        : kind === 'fall'
          ? [{ className: 'quality-bar-now', share: part }]
          : [];
    return (
      `<div class="quality-tile op-glass op-tile" data-kind="${escape(kind)}">` +
      `<span class="quality-tile-name">${escape(name)}</span>` +
      `<strong class="quality-tile-value">${escape(value)}</strong>` +
      bar(parts, kind) +
      `<span class="quality-tile-note">${escape(note)}</span></div>`
    );
  });
  return (
    `<p class="quality-held" data-all="${all}"><span aria-hidden="true"></span>${escape(said)}</p>` +
    `<div class="quality-tiles">${drawn.join('')}</div>\n`
  );
}

/** The rulebook's cards: the title, the promise, the example, the gauge and the count under it. */
function groups(fence: string): string {
  const cards = rows(fence).map(([title = '', promise = '', example = '', ...numbers]) => {
    const [gated = 0, rules = 0, passed = 0, run = 0] = numbers.map(Number);
    const ran =
      passed === run
        ? `<strong>${count(passed)}</strong>`
        : `<strong>${count(passed)} of ${count(run)}</strong>`;
    return (
      `<article class="quality-group op-glass op-tile"><h3>${escape(title)}</h3>` +
      `<p>${escape(promise)}</p><p class="quality-example">${escape(example)}</p>` +
      bar([{ className: 'quality-bar-now', share: rules ? (100 * gated) / rules : 0 }], 'gauge') +
      `<p class="quality-group-count"><strong>${count(gated)} of ${count(rules)}</strong> rules checked by the gate` +
      ` <span aria-hidden="true">·</span> ${ran} runs passed</p></article>`
    );
  });
  return `<div class="quality-groups">${cards.join('')}</div>\n`;
}

/** The block for one fence of the Quality page. */
export function qualityBlock(name: QualityFence, fence: string, head = ''): string {
  if (name === 'quality-readouts') return readouts(fence, head);
  if (name === 'quality-tiles') return tiles(fence);
  if (name === 'quality-groups') return groups(fence);
  return trendFigure(parseTrend(fence));
}
// #endregion quality-blocks
