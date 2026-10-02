/**
 * A horizontal meter: a share of a ceiling as a filled bar, with its name, its
 * ceiling and its reading in words, read to a screen reader as one meter. The
 * share comes from gaugeMeter in src/lib/gauge.ts. Its own component because
 * the machines and spend cards use it with no chart around it.
 */
import type { CSSProperties } from 'react';
import { gaugeMeter } from '../../../lib/gauge';
import styles from '../charts/charts.module.css';

// #region bar-gauge
/**
 * A share of a ceiling as a horizontal bar, such as memory used of its limit.
 * The name and the ceiling sit above the bar. The reading is printed just past
 * the end of the fill, or inside the fill once the fill reaches 40%. A gold bar
 * always prints its reading under the track instead, because neither white nor
 * the heading text colour has enough contrast on gold.
 *
 * To a screen reader the whole bar is one meter, named by the gauge's name and
 * read as "reading of ceiling". The visible reading is hidden from it so it is
 * not heard twice.
 */
export function BarGauge({
  testId,
  name,
  ceiling,
  value,
  max,
  reading,
  tone = 'deep',
}: {
  testId: string;
  name: string;
  /** The ceiling in words, shown on the right of the name: "1,183 MB". */
  ceiling: string;
  value: number;
  max: number;
  /** The reading in words: "31 % · 364 MB". */
  reading: string;
  /** The fill colour: deep teal, or gold for request units. */
  tone?: 'deep' | 'gold';
}) {
  const meter = gaugeMeter(value, max);
  // Where the reading goes: under the track for gold, otherwise inside or after the fill.
  const below = tone === 'gold';
  const inside = !below && meter.share >= 0.4;
  // The fill's width is a CSS custom property, so the stylesheet does the drawing.
  const fill = { '--gauge-share': `${(meter.share * 100).toFixed(1)}%` } as CSSProperties;
  const nameId = `${testId}-name`;
  return (
    <div className={styles.gauge} data-testid={testId} data-tone={tone}>
      <div className={styles.gaugeHead}>
        <span className={styles.gaugeName} id={nameId}>
          {name}
        </span>
        <span className={styles.gaugeCeiling}>{ceiling}</span>
      </div>
      <div
        className={styles.gaugeTrack}
        role="meter"
        aria-labelledby={nameId}
        aria-valuemin={0}
        aria-valuemax={meter.max}
        aria-valuenow={meter.now}
        aria-valuetext={`${reading} of ${ceiling}`}
        style={fill}
      >
        <span className={`${styles.gaugeFill} ${tone === 'gold' ? styles.gaugeGold : ''}`} />
        {!below && (
          <span
            className={inside ? styles.gaugeInside : styles.gaugeAfter}
            data-testid={`${testId}-reading`}
            data-inside={inside ? 'true' : 'false'}
            aria-hidden="true"
          >
            {reading}
          </span>
        )}
      </div>
      {below && (
        <span
          className={styles.gaugeBelow}
          data-testid={`${testId}-reading`}
          data-inside="false"
          aria-hidden="true"
        >
          {reading}
        </span>
      )}
    </div>
  );
}
// #endregion bar-gauge
