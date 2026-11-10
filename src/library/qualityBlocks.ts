/**
 * Does:      Turns the Quality page's `quality-trend` fence into a figure: a caption with the number's name and the
 *            way it may move, a sentence from its first reading to its last, and the placeholder the line is drawn
 *            into, carrying the readings as data (ADR-130).
 * Does not:  Read the history (the API writes the readings into the fence, QualityCounts.cs), draw the line
 *            (useLiveBlocks.tsx mounts QualityTrend), or hold a colour of its own.
 * Used by:   markdown.ts, qualityBlocks.test.ts.
 */
import { format, parseTrend } from '../lib/qualityTrend';

// #region quality-blocks
/** The fences this file draws, by the name on the fence. */
export const QUALITY_FENCES = ['quality-trend'] as const;
export type QualityFence = (typeof QUALITY_FENCES)[number];

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** A figure for one number: its caption, its first and last readings in words, and the chart's placeholder. */
export function qualityBlock(_name: QualityFence, fence: string): string {
  const data = parseTrend(fence);
  const first = data.points[0];
  const last = data.points[data.points.length - 1];
  const words =
    first && last
      ? `${format(first.value)} on ${first.date}${first.preScan ? ' (the pre-scan)' : ` at ${first.version}`}, ${format(last.value)} at ${last.version || last.date}, over ${data.points.length} readings.`
      : 'No readings in this build.';
  return (
    `<figure class="quality-trend">` +
    `<figcaption><strong>${escape(data.label)}</strong> <span>${escape(data.direction)}</span></figcaption>` +
    `<div data-quality-trend="${escape(JSON.stringify(data))}"></div>` +
    `<p>${escape(words)}</p>` +
    `</figure>\n`
  );
}
// #endregion quality-blocks
