/**
 * Does:      Draws a card's window as the approved canvas "Admin card chart redesign" does: one panel a series when
 *            each series has its own lines, stacked on one time axis, each titled with its name and ending in its
 *            newest reading; under them the time axis with the rolls and the tallies as counted ticks, a day label
 *            under every day and a "now" mark at the right edge; and the legend saying only what the chart does not.
 *            It measures its own width, so the words on it stay the size the page sets. A missing bucket breaks the
 *            line rather than drawing a zero nobody measured.
 * Does not:  Place points, marks or labels (src/lib/cloud.ts and src/lib/cloudChart.ts do), draw a panel's inside
 *            (CloudPanel.tsx does), or draw the dials.
 * Used by:   src/components/admin/CardShell/CardShell.tsx, through index.ts.
 */
import { type ReactNode, type RefObject, useEffect, useRef, useState } from 'react';
import { xOf, type CloudReport } from '../../../lib/cloud';
import {
  dayLabels,
  isWide,
  joinClose,
  legendRest,
  markKey,
  midnights,
  nowLabel,
  panelsOf,
  spaced,
  ticksOf,
  windowOf,
} from '../../../lib/cloudChart';
import { CloudPanel, LEFT, RIGHT } from './CloudPanel';
import styles from './CloudChart.module.css';

/** The width a chart is drawn at before the page has measured it, and in a test with no layout. */
const FALLBACK_WIDTH = 560;
/** Below this the axis prints two-letter day names. */
const NARROW = 480;
const AXIS = 60;
/** Where the time axis's line runs: the rolls' counted dots sit on it, the tallies and changes stand above it. */
const BASE = 22;

/** The figure's own width, measured, so text on the chart stays the size the page sets rather than scaling with it. */
function useWidth(): [RefObject<HTMLElement | null>, number] {
  const ref = useRef<HTMLElement | null>(null);
  const [width, setWidth] = useState(FALLBACK_WIDTH);
  useEffect(() => {
    const element = ref.current;
    if (element === null || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver((entries) => {
      const seen = Math.floor(entries[0]?.contentRect.width ?? 0);
      if (seen > 0) setWidth(seen);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  return [ref, width];
}

export function CloudChart({
  chart,
  endMs,
  toolbar,
}: {
  chart: CloudReport['chart'];
  endMs: number;
  toolbar: ReactNode;
}) {
  const window = windowOf(chart.start_ms, endMs);
  const [ref, width] = useWidth();
  const x = (at: number) => LEFT + xOf(at, chart.start_ms, endMs, width - LEFT - RIGHT);
  const panels = panelsOf(chart);
  const hasPeaks = chart.series.some((s) => s.points.some((p) => p.peak != null));
  const height = panels.length > 1 ? 104 : 140;
  const short = width < NARROW;
  const ticks = joinClose(ticksOf(chart.marks, window), x, 16);
  const days = spaced(dayLabels(chart.start_ms, endMs, window, short), x, short ? 38 : 52);
  const bucket = window === '30d' ? 'four hours’' : 'hour’s';
  const how =
    isWide(window) && hasPeaks
      ? short
        ? `${window === '30d' ? '4-hour' : 'hourly'} average, peak shaded`
        : `each ${bucket} average, its peak shaded`
      : '';
  return (
    <figure className={styles.chart} data-testid="cloud-chart" ref={ref}>
      <div className={styles.head}>
        <span className={styles.title}>{chart.title}</span>
        {toolbar}
      </div>
      {panels.map((panel) => (
        <div key={panel.name} className={styles.panelBox}>
          <p className={styles.panelHead}>
            <span>
              <span className={styles.panelName}>{panel.name}</span>
              {how === '' ? '' : ` · ${how}`}
            </span>
            {panel.series.length === 1 && (
              <span className={styles.now}>{nowLabel(panel.series[0])}</span>
            )}
          </p>
          <CloudPanel
            chart={chart}
            panel={panel}
            window={window}
            width={width}
            height={height}
            x={x}
            endMs={endMs}
          />
        </div>
      ))}
      <svg
        className={styles.axisTrack}
        width={width}
        height={AXIS}
        viewBox={`0 0 ${width} ${AXIS}`}
        role="img"
        aria-label={`Time axis, ${ticks.length} counted marks`}
      >
        <line className={styles.baseline} x1={LEFT} x2={width - RIGHT} y1={BASE} y2={BASE} />
        {midnights(chart.start_ms, endMs).map((at) => (
          <line
            key={at}
            className={styles.baseline}
            x1={x(at)}
            x2={x(at)}
            y1={BASE}
            y2={BASE + 6}
          />
        ))}
        {ticks.map((tick) =>
          tick.kind === 'deploy' ? (
            <g
              key={`d-${tick.atMs}`}
              className={styles.rollTick}
              data-testid="cloud-tick"
              data-kind="deploy"
            >
              <circle cx={x(tick.atMs)} cy={BASE} r={7} />
              <text x={x(tick.atMs)} y={BASE + 3.5} textAnchor="middle">
                {tick.count}
              </text>
              <title>{tick.titles.join('\n')}</title>
            </g>
          ) : (
            <g
              key={`${tick.kind}-${tick.atMs}`}
              className={tick.kind === 'change' ? styles.changeTick : styles.countTick}
              data-testid="cloud-tick"
              data-kind={tick.kind}
            >
              <line x1={x(tick.atMs)} x2={x(tick.atMs)} y1={2} y2={12} />
              {tick.count > 1 && (
                <text x={x(tick.atMs) + 3} y={10} textAnchor="start">
                  {tick.count}
                </text>
              )}
              <title>{tick.titles.join('\n')}</title>
            </g>
          )
        )}
        {days.map((label) => (
          <text
            key={label.atMs}
            className={styles.day}
            x={x(label.atMs)}
            y={BASE + 20}
            textAnchor="middle"
            data-testid="cloud-day"
          >
            {label.text}
          </text>
        ))}
        <g className={styles.nowMark} data-testid="cloud-now">
          <line x1={width - RIGHT} x2={width - RIGHT} y1={BASE - 8} y2={BASE + 8} />
          <text x={width - RIGHT} y={AXIS - 3} textAnchor="end">
            now
          </text>
        </g>
      </svg>
      <figcaption className={styles.legend}>
        <ul className={styles.legendChips} aria-label="Legend">
          {[...markKey(chart, window), ...legendRest(chart.legend)].map((chip) => (
            <li key={chip} className={styles.legendChip}>
              {chip}
            </li>
          ))}
        </ul>
      </figcaption>
    </figure>
  );
}
