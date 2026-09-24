/**
 * The chart the traffic and machines cards draw with, and the box that reads a
 * minute out of it (ADR: What the machines are doing).
 */
import { useState } from 'react';
import {
  axisLabel,
  ceilingFor,
  type ChartSeries,
  gridHeights,
  type KeptBucket,
  LEAST_SLOTS,
  MACHINE_CHART,
  type MachineWindow,
  nearestIndex,
  pathFor,
  readoutLine,
  ticks,
  xOf,
} from '../../lib/machineChart';
import styles from '../AdminPanel.module.css';

// #region chart-readout
/**
 * What a chart says under a pointer or a finger (ADR: The glass look): a rule
 * at the slot, the slot's time, and each line's reading there in the unit the
 * axis is in. A slot nobody measured says so, because a gap is a gap. The box
 * sits to the right of the rule until it would leave the drawing, then to the
 * left. It takes no pointer events, so it never steals the hover it is showing.
 */
/** Under the unit at the top of the axis, so the box never covers the word that says what it is counting. */
export const READOUT_DROP = 14;

export function ChartReadout({
  testId,
  x,
  when,
  rows,
  unit,
}: {
  testId: string;
  x: number;
  when: string;
  rows: { name: string; value: number | null }[];
  unit?: string;
}) {
  const lines = [when, ...rows.map((row) => readoutLine(row.name, row.value, unit))];
  const width = Math.min(360, Math.max(...lines.map((line) => line.length)) * 6.2 + 16);
  const height = lines.length * 14 + 10;
  const left = x + 8 + width > MACHINE_CHART.width - MACHINE_CHART.right ? x - 8 - width : x + 8;
  return (
    <g className={styles.readout} data-testid={`${testId}-readout`} aria-hidden="true">
      <line
        className={styles.readoutRule}
        x1={x}
        y1={MACHINE_CHART.top}
        x2={x}
        y2={MACHINE_CHART.height - MACHINE_CHART.bottom}
      />
      <rect
        className={styles.readoutBox}
        x={left}
        y={MACHINE_CHART.top + READOUT_DROP}
        width={width}
        height={height}
        rx="4"
      />
      {lines.map((line, index) => (
        <text
          key={line}
          className={index === 0 ? styles.readoutWhen : styles.readoutText}
          x={left + 8}
          y={MACHINE_CHART.top + READOUT_DROP + 15 + index * 14}
        >
          {line}
        </text>
      ))}
    </g>
  );
}

/**
 * One resource, one chart (ADR: What the machines are doing, the addendum on
 * drawing them). Up to two lines on one axis, drawn the way the activity graph
 * is drawn and with the same arithmetic split out into src/lib/machineChart.ts.
 * A percentage chart keeps a full axis whatever the hour held, so a quiet hour
 * looks quiet; anything else takes its ceiling from the readings.
 */
export function MachineChart({
  testId,
  label,
  series,
  percentage,
  unit,
  window: drawnWindow = '1h',
  tones,
  axisUnit,
}: {
  testId: string;
  label: string;
  series: ChartSeries[];
  percentage?: boolean;
  unit?: string;
  window?: MachineWindow;
  /**
   * A colour per line. A line is a series, and a series takes an identity
   * colour: 'first', 'second' and 'third' are the series tokens, in their fixed
   * order, which are not a store's and not a status; the third is the neutral
   * grey, and it is what a turned-away request is drawn in. 'bad' is the one
   * state a line can be, a server error, which is something wrong whenever it
   * is above zero. There is no good line and no
   * warning line: a slow series drawn in the warning colour reads as an alarm
   * to somebody scanning the page (ADR: The Admin tab, as a product, the
   * addendum on the traffic card in plain words).
   */
  tones?: ('first' | 'second' | 'third' | 'bad')[];
  /** The unit, written at the top of the axis, so a number on the axis is a number of something. */
  axisUnit?: string;
}) {
  // The slot a pointer or a finger is over, for the readout; none until one is.
  const [over, setOver] = useState<number | null>(null);
  const ceiling = ceilingFor(series, percentage === true ? 100 : 1);
  const points = series[0]?.points ?? [];
  const drawn = series.some((line) => line.points.some((point) => point.value !== null));
  const innerWidth = MACHINE_CHART.width - MACHINE_CHART.left - MACHINE_CHART.right;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  const colour = (index: number) => {
    const tone = tones?.[index];
    if (tone === 'first') return styles.firstLine;
    if (tone === 'second') return styles.secondLine;
    if (tone === 'third') return styles.thirdLine;
    if (tone === 'bad') return styles.badLine;
    return index === 0 ? styles.allLine : index === 1 ? styles.sqlLine : styles.cosmosLine;
  };

  if (!drawn) {
    return (
      <p className={styles.muted} data-testid={`${testId}-empty`}>
        Nothing to draw yet.
      </p>
    );
  }

  return (
    <>
      <svg
        className={styles.chart}
        viewBox={`0 0 ${MACHINE_CHART.width} ${MACHINE_CHART.height}`}
        role="img"
        aria-label={label}
        data-testid={testId}
        onPointerMove={(event) => {
          const box = event.currentTarget.getBoundingClientRect();
          if (box.width <= 0) return;
          setOver(
            nearestIndex(
              ((event.clientX - box.left) / box.width) * MACHINE_CHART.width,
              points.length
            )
          );
        }}
        onPointerLeave={() => setOver(null)}
      >
        {gridHeights().map((y) => (
          <line
            key={y}
            className={styles.gridLine}
            x1={MACHINE_CHART.left}
            y1={y}
            x2={MACHINE_CHART.width - MACHINE_CHART.right}
            y2={y}
          />
        ))}
        <line
          className={styles.axis}
          x1={MACHINE_CHART.left}
          y1={MACHINE_CHART.height - MACHINE_CHART.bottom}
          x2={MACHINE_CHART.width - MACHINE_CHART.right}
          y2={MACHINE_CHART.height - MACHINE_CHART.bottom}
        />
        <line
          className={styles.axis}
          x1={MACHINE_CHART.left}
          y1={MACHINE_CHART.top}
          x2={MACHINE_CHART.left}
          y2={MACHINE_CHART.height - MACHINE_CHART.bottom}
        />
        <text
          className={styles.axisLabel}
          x={MACHINE_CHART.left - 4}
          y={MACHINE_CHART.top + 4}
          textAnchor="end"
        >
          {ceiling}
          {percentage === true ? '%' : ''}
        </text>
        <text
          className={styles.axisLabel}
          x={MACHINE_CHART.left - 4}
          y={MACHINE_CHART.height - MACHINE_CHART.bottom}
          textAnchor="end"
        >
          0
        </text>
        {ticks(points.length).map((index) => (
          <text
            key={index}
            className={styles.axisLabel}
            x={MACHINE_CHART.left + index * step}
            y={MACHINE_CHART.height - 8}
            textAnchor={index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle'}
          >
            {points[index] ? axisLabel(points[index].at, drawnWindow) : ''}
          </text>
        ))}
        {series.map((line, index) => (
          <g
            key={line.key}
            className={colour(index)}
            data-testid={`${testId}-${line.key}`}
            data-tone={tones?.[index] ?? 'position'}
          >
            <path className={styles.line} d={pathFor(line.points, ceiling)} />
          </g>
        ))}
        {axisUnit !== undefined && (
          <text
            className={`${styles.axisLabel} ${styles.axisUnit}`}
            x={MACHINE_CHART.left + 6}
            y={MACHINE_CHART.top + 4}
            data-testid={`${testId}-unit`}
          >
            {axisUnit}
          </text>
        )}
        {over !== null && points[over] !== undefined && (
          <ChartReadout
            testId={testId}
            x={xOf(over, points.length)}
            when={axisLabel(points[over].at, drawnWindow)}
            rows={series.map((line) => ({
              name: line.name,
              value: line.points[over]?.value ?? null,
            }))}
            unit={axisUnit ?? (percentage === true ? '%' : unit)}
          />
        )}
      </svg>
      <ul className={styles.summaryList}>
        {series.map((line, index) => (
          <li key={line.key}>
            <span className={`${styles.swatch} ${colour(index)}`} aria-hidden="true" />
            {line.name}
            {unit === undefined ? '' : ` (${unit})`}
          </li>
        ))}
      </ul>
    </>
  );
}

/**
 * Traffic, drawn (ADR: The Admin tab, as a product). Four numbers in plain
 * words, then the three questions a person opening this tab is asking, each a
 * section with the question as its title, one sentence on how to read it and
 * one chart in the frame the machine charts use: did anything fail, which
 * comes first because it is the one somebody opens the tab to find out, how
 * busy is it, and how fast is it answering. What the words are is decided in
 * src/lib/trafficCard.ts, which has no React in it. The hour is the request ring a minute at a time; a
 * wider window is the minutes each site keeps. Both are turned into the same
 * slots by src/lib/machineChart.ts, so a gap is a gap and a zero is a zero in
 * every window. A build with no traffic block, or a window nothing is kept
 * for, keeps its sentence and draws nothing, rather than pretending to a line.
 */
/** The day and the time a kept window's drawing starts at, for the sentence that says so. */
export function startLabel(at: string): string {
  return new Date(at).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * Where a trimmed window's drawing starts, in words. The drawing starts at the
 * first reading unless that would leave fewer than a dozen slots, and then it
 * starts a dozen back; 1.0.0.166 called both "its first reading" and on its
 * first day was wrong by two days, on the live page, in its own sentence.
 */
export function youngRecord(drawn: { at: string; bucket: KeptBucket | null }[]): string {
  const first = drawn.find((slot) => slot.bucket !== null);
  return first === undefined || first.at === drawn[0].at
    ? `The record is younger than the window, so the charts start at its first reading, ${startLabel(drawn[0].at)}`
    : `The record is younger than the window: its first reading is ${startLabel(first.at)}, and the charts start ${LEAST_SLOTS} buckets back from now, at ${startLabel(drawn[0].at)}`;
}
