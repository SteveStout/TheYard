/**
 * Shared chart pieces for the Admin tab's cards (ADR-078). MachineChart draws up
 * to two lines on one axis; the traffic and machines cards both use it.
 * ChartReadout is the box that shows a minute's values under the pointer,
 * BarGauge is a horizontal meter, and useFittedBox sizes a chart to its space.
 * The arithmetic (scales, paths, ticks) lives in src/lib/machineChart.ts and
 * src/lib/plotFrame.ts, which have no React in them. This file only draws.
 */
import { type CSSProperties, useLayoutEffect, useState } from 'react';
import {
  axisLabel,
  ceilingFor,
  type ChartSeries,
  type KeptBucket,
  calloutFor,
  machineFrame,
  LEAST_SLOTS,
  MACHINE_CHART,
  type MachineWindow,
  nearestIndex,
  pathFor,
  readoutLine,
  ticks,
  xOf,
} from '../../../lib/machineChart';
import { gaugeMeter } from '../../../lib/gauge';
import { fitBox, type plotFrame, type PlotBox } from '../../../lib/plotFrame';
import styles from '../AdminPanel/AdminPanel.module.css';

// #region fitted-box
/**
 * Sizes a chart's drawing box to the width its svg actually gets. On a desk
 * that is the box's own width; on a phone it is the phone's width, so the
 * chart's text keeps its size instead of shrinking (see fitBox in
 * src/lib/plotFrame.ts). It measures before the first paint and again on every
 * resize, so the chart is drawn once at the right width and never jumps.
 *
 * Returns a ref callback to put on the svg, and the fitted box.
 */
export function useFittedBox<Box extends PlotBox>(
  box: Box
): [(node: SVGSVGElement | null) => void, Box] {
  const [node, setNode] = useState<SVGSVGElement | null>(null);
  const [given, setGiven] = useState(0);
  // useLayoutEffect runs after the DOM is built but before the browser paints,
  // so the first frame the user sees is already at the measured width.
  useLayoutEffect(() => {
    if (node === null) return;
    const measure = () => setGiven(node.getBoundingClientRect().width);
    measure();
    const watcher = new ResizeObserver(measure);
    watcher.observe(node);
    return () => watcher.disconnect();
  }, [node]);
  return [setNode, fitBox(box, given)];
}
// #endregion fitted-box

// #region chart-readout
/** How far the readout box sits below the plot's top, so it never covers the axis unit. */
export const READOUT_DROP = 14;

/**
 * What a chart shows under a pointer or a finger (ADR-081): a vertical rule at
 * that minute, the minute's time, and each line's value in the axis unit. A
 * minute nobody measured says so, because a gap is not a zero. The box sits to
 * the right of the rule, or to the left when it would run off the drawing. It is
 * aria-hidden and takes no pointer events, so it never steals the hover that
 * shows it.
 */
export function ChartReadout({
  testId,
  x,
  when,
  rows,
  unit,
  box = MACHINE_CHART,
}: {
  testId: string;
  x: number;
  when: string;
  rows: { name: string; value: number | null }[];
  unit?: string;
  box?: PlotBox;
}) {
  const lines = [when, ...rows.map((row) => readoutLine(row.name, row.value, unit))];
  // Size the box to its longest line: about 6.2 units per character plus padding.
  const width = Math.min(360, box.width, Math.max(...lines.map((line) => line.length)) * 6.2 + 16);
  const height = lines.length * 14 + 10;
  // Beside the rule, on the side with room, and never past either edge of a narrow drawing.
  const beside = x + 8 + width > box.width - box.right ? x - 8 - width : x + 8;
  const left = Math.max(0, Math.min(beside, box.width - width));
  return (
    <g className={styles.readout} data-testid={`${testId}-readout`} aria-hidden="true">
      <line
        className={styles.readoutRule}
        x1={x}
        y1={box.top}
        x2={x}
        y2={box.height - box.bottom}
      />
      <rect
        className={styles.readoutBox}
        x={left}
        y={box.top + READOUT_DROP}
        width={width}
        height={height}
        rx="4"
      />
      {lines.map((line, index) => (
        <text
          key={line}
          className={index === 0 ? styles.readoutWhen : styles.readoutText}
          x={left + 8}
          y={box.top + READOUT_DROP + 15 + index * 14}
        >
          {line}
        </text>
      ))}
    </g>
  );
}

/**
 * One resource, one chart (ADR-078): up to two lines on one axis, drawn the same
 * way as the activity graph. A percentage chart always runs its axis to 100%, so
 * a quiet hour looks quiet. Any other chart takes its top from the readings.
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
  callout,
}: {
  testId: string;
  label: string;
  series: ChartSeries[];
  percentage?: boolean;
  unit?: string;
  window?: MachineWindow;
  /**
   * A colour per line. 'first', 'second' and 'third' are the fixed series
   * colours: they tell lines apart and mean nothing else. The third is the
   * neutral grey, used for turned-away requests. 'bad' is for server errors,
   * which are a problem whenever they are above zero. There is deliberately no
   * warning colour: a slow series drawn in amber reads as an alarm to someone
   * scanning the page (ADR-080).
   */
  tones?: ('first' | 'second' | 'third' | 'bad')[];
  /** The unit written at the top of the axis, so each axis number is a number of something. */
  axisUnit?: string;
  /** Marks the highest reading of one series with a gold leader line and a small label. */
  callout?: { key: string; name: string };
}) {
  // The slot a pointer or a finger is over, for the readout; none until one is.
  const [over, setOver] = useState<number | null>(null);
  const [fit, box] = useFittedBox(MACHINE_CHART);
  const ceiling = ceilingFor(series, percentage === true ? 100 : 1);
  const points = series[0]?.points ?? [];
  const drawn = series.some((line) => line.points.some((point) => point.value !== null));
  const innerWidth = box.width - box.left - box.right;
  const step = points.length <= 1 ? 0 : innerWidth / (points.length - 1);
  const peak =
    callout === undefined ? null : calloutFor(series, callout, ceiling, drawnWindow, box);
  // A single series has no legend, so when the caller named no axis unit, the
  // series' own unit goes at the top of the axis instead.
  const shownUnit = axisUnit ?? (series.length === 1 && percentage !== true ? unit : undefined);
  // The CSS class for a line's colour: its tone if given, otherwise its position.
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
        ref={fit}
        className={styles.chart}
        viewBox={`0 0 ${box.width} ${box.height}`}
        role="img"
        aria-label={peak === null ? label : `${label}; ${peak.label}`}
        data-testid={testId}
        onPointerMove={(event) => {
          // Turn the pointer's screen position into drawing units, then into the nearest slot.
          const rect = event.currentTarget.getBoundingClientRect();
          if (rect.width <= 0) return;
          setOver(
            nearestIndex(((event.clientX - rect.left) / rect.width) * box.width, points.length, box)
          );
        }}
        onPointerLeave={() => setOver(null)}
      >
        {/* The instrument style: no grid, graduation ticks on the axes, and gold
            corner brackets at the plot's top left and bottom right. */}
        <PlotFrame frame={machineFrame(points.length, box)} testId={testId} />
        <line
          className={styles.axis}
          x1={box.left}
          y1={box.height - box.bottom}
          x2={box.width - box.right}
          y2={box.height - box.bottom}
        />
        <line
          className={styles.axis}
          x1={box.left}
          y1={box.top}
          x2={box.left}
          y2={box.height - box.bottom}
        />
        <text className={styles.axisLabel} x={box.left - 8} y={box.top + 4} textAnchor="end">
          {ceiling}
          {percentage === true ? '%' : ''}
        </text>
        <text
          className={styles.axisLabel}
          x={box.left - 8}
          y={box.height - box.bottom}
          textAnchor="end"
        >
          0
        </text>
        {ticks(points.length).map((index) => (
          <text
            key={index}
            className={styles.axisLabel}
            x={box.left + index * step}
            y={box.height - 8}
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
            <path className={styles.line} d={pathFor(line.points, ceiling, box)} />
          </g>
        ))}
        {peak !== null && <PeakCallout drawn={peak} testId={testId} />}
        {shownUnit !== undefined && (
          <text
            className={`${styles.axisLabel} ${styles.axisUnit}`}
            x={box.left}
            y={box.top - 3}
            data-testid={`${testId}-unit`}
          >
            {shownUnit}
          </text>
        )}
        {over !== null && points[over] !== undefined && (
          <ChartReadout
            testId={testId}
            x={xOf(over, points.length, box)}
            when={axisLabel(points[over].at, drawnWindow)}
            rows={series.map((line) => ({
              name: line.name,
              value: line.points[over]?.value ?? null,
            }))}
            unit={axisUnit ?? (percentage === true ? '%' : unit)}
            box={box}
          />
        )}
      </svg>
      {/* Two or more series get a legend. One series needs none: the chart's title names it. */}
      {series.length > 1 && (
        <ul className={styles.legend} data-testid={`${testId}-legend`}>
          {series.map((line, index) => (
            <li key={line.key}>
              <span className={`${styles.swatch} ${colour(index)}`} aria-hidden="true" />
              {line.name}
              {unit === undefined ? '' : ` (${unit})`}
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

/** Draws the instrument-style frame: axis ticks and corner brackets from src/lib/plotFrame.ts. */
export function PlotFrame({
  frame,
  testId,
}: {
  frame: ReturnType<typeof plotFrame>;
  testId?: string;
}) {
  return (
    <>
      {frame.ticks.map((tick) => (
        <line
          key={tick.key}
          className={tick.major ? `${styles.markTick} ${styles.markTickMajor}` : styles.markTick}
          x1={tick.x1}
          y1={tick.y1}
          x2={tick.x2}
          y2={tick.y2}
          data-testid={testId === undefined ? undefined : `${testId}-tick`}
        />
      ))}
      {frame.brackets.map((d) => (
        <path
          key={d}
          className={styles.plotBracket}
          d={d}
          data-testid={testId === undefined ? undefined : `${testId}-bracket`}
        />
      ))}
    </>
  );
}

/** Draws the peak callout: a dot, a leader line and a label, all placed by calloutFor. */
function PeakCallout({
  drawn,
  testId,
}: {
  drawn: NonNullable<ReturnType<typeof calloutFor>>;
  testId: string;
}) {
  return (
    <g className={styles.callout} data-testid={`${testId}-callout`}>
      <polyline className={styles.calloutLine} points={drawn.leader} />
      <circle className={styles.calloutDot} cx={drawn.dot.x} cy={drawn.dot.y} r={3} />
      <text
        className={styles.calloutText}
        x={drawn.text.x}
        y={drawn.text.y}
        textAnchor={drawn.text.anchor}
      >
        {drawn.label}
      </text>
    </g>
  );
}

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

/** The date and time a kept window's chart starts at, for the sentence that says so. */
export function startLabel(at: string): string {
  return new Date(at).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/**
 * The sentence for a window that reaches back further than the record does.
 * The chart starts at the first reading, unless that would leave fewer than
 * LEAST_SLOTS slots; then it starts LEAST_SLOTS buckets back from now. The two
 * cases need different sentences, or the page states the wrong start date.
 */
export function youngRecord(drawn: { at: string; bucket: KeptBucket | null }[]): string {
  const first = drawn.find((slot) => slot.bucket !== null);
  return first === undefined || first.at === drawn[0].at
    ? `The record is younger than the window, so the charts start at its first reading, ${startLabel(drawn[0].at)}`
    : `The record is younger than the window: its first reading is ${startLabel(first.at)}, and the charts start ${LEAST_SLOTS} buckets back from now, at ${startLabel(drawn[0].at)}`;
}
