/**
 * Does:      Draws one of the Quality page's charts as its picture draws it (ADR-130, the addendum on the page as its
 *            picture): labelled gridlines, the line in teal through readings spaced by their day, the version the
 *            number joined the ratchet as a gold mark, a dot at today and a dashed floor where the fence names one;
 *            the page's first chart adds the slip, from the first reading to the ratchet, "THE RATCHET" with its
 *            day, and three callouts, no version anywhere, and on a phone it is drawn at its own taller shape.
 * Does not:  Place anything (src/lib/qualityChart.ts does) or read the history (the fence carries the readings).
 * Used by:   useLiveBlocks.tsx.
 */
import { useId } from 'react';
import {
  chartGeometry,
  SLIP_PHONE,
  SLIP_WIDE,
  SMALL,
  type ChartBox,
} from '../../../lib/qualityChart';
import type { QualityTrendData } from '../../../lib/qualityTrend';
import styles from './QualityChart.module.css';

/** One drawing of the chart in one box. */
function Drawing({
  data,
  box,
  phone,
  className,
}: {
  data: QualityTrendData;
  box: ChartBox;
  phone: boolean;
  className: string;
}) {
  const id = useId();
  const shape = chartGeometry(data, box, phone);
  const { plot } = shape;
  return (
    <svg
      className={`${styles.chart} ${className}`}
      viewBox={`0 0 ${box.width} ${box.height}`}
      role="img"
      aria-label={shape.summary}
      data-testid="quality-chart"
      data-mock={data.form === 'slip' ? 'slip-chart' : undefined}
    >
      <defs>
        <linearGradient id={`${id}-area`} x1="0" y1="0" x2="0" y2="1">
          <stop className={styles.areaTop} offset="0" />
          <stop className={styles.areaFoot} offset="1" />
        </linearGradient>
        <linearGradient id={`${id}-gold`} x1="0" y1="0" x2="0" y2="1">
          <stop className={styles.goldTop} offset="0" />
          <stop className={styles.goldMid} offset="0.5" />
          <stop className={styles.goldFoot} offset="1" />
        </linearGradient>
      </defs>
      {shape.slip && (
        <>
          <rect
            className={styles.slip}
            x={plot.left}
            y={plot.top}
            width={Math.max(0, shape.slip.right - plot.left)}
            height={plot.bottom - plot.top}
          />
          <text
            className={styles.band}
            x={(plot.left + shape.slip.right) / 2}
            y={plot.bottom - 12}
            textAnchor="middle"
          >
            {shape.slip.words}
          </text>
        </>
      )}
      {shape.grid.map((tick) => (
        <g key={tick.y}>
          <line className={styles.grid} x1={plot.left} x2={plot.right} y1={tick.y} y2={tick.y} />
          <text className={styles.tick} x={plot.left - 10} y={tick.y + 4} textAnchor="end">
            {tick.text}
          </text>
        </g>
      ))}
      {shape.area && <path d={shape.area} fill={`url(#${id}-area)`} />}
      {shape.floor && (
        <g data-testid="quality-chart-floor">
          <line
            className={styles.floor}
            x1={plot.left}
            x2={plot.right}
            y1={shape.floor.y}
            y2={shape.floor.y}
          />
          <text className={styles.mark} x={plot.right} y={shape.floor.y - 5} textAnchor="end">
            {shape.floor.words}
          </text>
        </g>
      )}
      {shape.preScan && <path className={`${styles.line} ${styles.pre}`} d={shape.preScan} />}
      <path className={styles.line} d={shape.line} data-testid="quality-chart-line" />
      {/* The ratchet's gold mark over the line, as the picture lays it: the line drops through it, never across it. */}
      {shape.join && (
        <g data-testid="quality-chart-join">
          <rect
            x={shape.join.x - 1.5}
            y={plot.top - 6}
            width={3}
            height={plot.bottom - plot.top + 6}
            fill={`url(#${id}-gold)`}
          />
          {shape.slip && (
            <text className={styles.mark} x={shape.join.x + 8} y={shape.join.y}>
              {shape.join.words}
            </text>
          )}
        </g>
      )}
      <line
        className={styles.axis}
        x1={plot.left}
        x2={plot.right}
        y1={plot.bottom}
        y2={plot.bottom}
      />
      {shape.callouts.map((callout) => {
        const nudge = callout.anchor === 'start' ? 6 : -6;
        return (
          <g key={`${callout.x}-${callout.words}`} data-testid="quality-chart-callout">
            <line
              className={styles.lead}
              x1={callout.x}
              y1={callout.y}
              x2={callout.tx}
              y2={callout.ty}
            />
            <circle className={styles.dot} cx={callout.x} cy={callout.y} r={5} />
            <text
              className={styles.value}
              x={callout.tx + nudge}
              y={callout.ty - 2}
              textAnchor={callout.anchor}
            >
              {callout.value}
            </text>
            <text
              className={styles.words}
              x={callout.tx + nudge}
              y={callout.ty + 14}
              textAnchor={callout.anchor}
            >
              {callout.words}
            </text>
          </g>
        );
      })}
      {shape.end && shape.callouts.length === 0 && (
        <circle
          className={styles.dot}
          cx={shape.end.x}
          cy={shape.end.y}
          r={4}
          data-testid="quality-chart-end"
        />
      )}
      {shape.xLabels.map((label) => (
        <text
          key={`${label.x}-${label.text}`}
          className={styles.tick}
          x={label.x}
          y={box.height - (data.form === 'slip' ? 12 : 8)}
          textAnchor={label.anchor}
        >
          {label.text}
        </text>
      ))}
    </svg>
  );
}

/** The chart for a fence of the Quality page: the first chart at its two shapes, or one small chart. */
export function QualityChart({ data }: { data: QualityTrendData }) {
  if (data.form === 'slip') {
    return (
      <>
        <Drawing data={data} box={SLIP_WIDE} phone={false} className={styles.wide} />
        <Drawing data={data} box={SLIP_PHONE} phone className={styles.phone} />
      </>
    );
  }
  return <Drawing data={data} box={SMALL} phone={false} className={styles.small} />;
}
