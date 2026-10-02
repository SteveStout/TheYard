/**
 * One resource, one chart: up to two lines on one axis, with its frame, its
 * axis labels, a legend for two or more lines, an optional callout on a peak
 * and a readout under the pointer. The arithmetic comes from
 * src/lib/machineChart.ts; this file only draws. The traffic and machines
 * cards both use it, so it is a component of its own.
 */
import { useState } from 'react';
import {
  axisLabel,
  calloutFor,
  ceilingFor,
  type ChartSeries,
  machineFrame,
  MACHINE_CHART,
  type MachineWindow,
  nearestIndex,
  pathFor,
  ticks,
  xOf,
} from '../../../lib/machineChart';
import { ChartReadout } from '../ChartReadout';
import { PeakCallout } from '../PeakCallout';
import { PlotFrame } from '../PlotFrame';
import { useFittedBox } from '../charts/useFittedBox';
import styles from '../charts/charts.module.css';
import cardStyles from '../shared/card.module.css';

/**
 * One resource, one chart (ADR: What the machines are doing): up to two lines
 * on one axis, drawn the same way as the activity graph. A percentage chart
 * always runs its axis to 100%, so a quiet hour looks quiet. Any other chart
 * takes its top from the readings.
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
   * scanning the page (ADR: The Admin tab, as a product).
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
      <p className={cardStyles.muted} data-testid={`${testId}-empty`}>
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
