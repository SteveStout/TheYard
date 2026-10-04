/**
 * The activity chart itself: visitor-days per day as stacked bands by kind, or as one
 * line per store, drawn as an inline SVG with its axes, the day labels, the band names,
 * the part of today still to come, and a crosshair with a readout under the pointer.
 * It is its own component because it holds all the chart's geometry and its hover state.
 */
import { type PointerEvent, useState } from 'react';
import {
  CHART,
  KIND_NAMES,
  areaPath,
  bandPath,
  ceilingOf,
  dayAt,
  edgePath,
  labelFor,
  labelSpot,
  labelledIndexes,
  linePath,
  partialDay,
  stackCeiling,
  todayNote,
  xAt,
  yAt,
  type ActivityBand,
  type ActivityReport,
  type ActivitySeries,
  type ActivityWho,
} from '../../../lib/activity';
import { plotFrame } from '../../../lib/plotFrame';
import styles from '../ActivityCard/ActivityCard.module.css';
import chartStyles from '../charts/charts.module.css';
import { PlotFrame, useFittedBox } from '../charts';
import { kindTone, storeName, storeTone } from '../ActivityCard/activitySeriesLook';
import { ActivityReadout, type ActivityReading } from '../ActivityReadout';

/**
 * Draws the report's days as bands (By kind) or lines (By store). The bands and lines
 * come in from the graph, which also uses them for the legend; the plot works out the
 * ceiling, the label positions and which day the pointer is on.
 */
export function ActivityPlot({
  report,
  who,
  view,
  bands,
  lines,
}: {
  report: ActivityReport;
  who: ActivityWho;
  view: 'kind' | 'store';
  bands: ActivityBand[];
  lines: ActivitySeries[];
}) {
  // The day under the pointer or the finger; null when there is none.
  const [hover, setHover] = useState<number | null>(null);
  const [fit, chart] = useFittedBox(CHART);
  const ceiling = view === 'kind' ? stackCeiling(bands) : ceilingOf(lines);
  const count = report.days.length;
  const labels = labelledIndexes(count);
  const innerWidth = chart.width - chart.left - chart.right;
  const step = count <= 1 ? 0 : innerWidth / (count - 1);
  // The report's own moment, not the browser's clock: rendering stays a pure function of
  // its props, and the part-day is the one the server counted.
  const partial = partialDay(report.days, new Date(report.until));
  // The day under the pointer, read off the drawing's own width, so a finger
  // and a mouse land on the same day in every engine.
  const point = (event: PointerEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    if (box.width === 0) return;
    setHover(dayAt(((event.clientX - box.left) / box.width) * chart.width, count, chart));
  };
  const hovered = hover !== null && hover < count ? hover : null;
  // What the crosshair reads out: each band's own share under By kind, each
  // line under By store, in the order the legend names them.
  const readings: ActivityReading[] =
    hovered === null
      ? []
      : view === 'kind'
        ? bands.map((band) => ({
            key: band.kind,
            className: kindTone(band.kind),
            name: KIND_NAMES[band.kind],
            value: band.upper[hovered] - band.lower[hovered],
            top: band.upper[hovered],
          }))
        : lines.map((line) => ({
            key: line.store,
            className: storeTone(line.store),
            name: line.store === 'all' ? line.name : storeName(report, line.store),
            value: line.points[hovered]?.requests ?? 0,
            top: line.points[hovered]?.requests ?? 0,
          }));
  return (
    <div className={styles.chartWrap}>
      <svg
        ref={fit}
        className={chartStyles.chart}
        viewBox={`0 0 ${chart.width} ${chart.height}`}
        onPointerMove={point}
        onPointerDown={point}
        onPointerLeave={(event) => {
          if (event.pointerType === 'mouse') setHover(null);
        }}
        role="img"
        aria-label={
          view === 'kind'
            ? `Visitor-days per day over the ${report.window} window, ${
                who === 'people'
                  ? 'people only'
                  : "all traffic, stacked: people, scanners and crawlers, the site's own reads"
              }`
            : `Unique visitors per day over the ${report.window} window, ${
                who === 'people' ? 'people only' : 'all traffic'
              }, everybody as one line and one line per store`
        }
        data-testid="activity-graph"
      >
        <line
          className={chartStyles.axis}
          x1={chart.left}
          y1={chart.height - chart.bottom}
          x2={chart.width - chart.right}
          y2={chart.height - chart.bottom}
        />
        <line
          className={chartStyles.axis}
          x1={chart.left}
          y1={chart.top}
          x2={chart.left}
          y2={chart.height - chart.bottom}
        />
        {/* The frame every framed chart shares (src/lib/plotFrame.ts): graduations up the
            side at the quarters, a tick under each day, and two gold bracket ticks at the
            corners. */}
        <PlotFrame
          frame={plotFrame(
            chart,
            report.days.map((day, index) => ({
              key: `d${day.day}`,
              x: xAt(index, count, chart),
              major: false,
            }))
          )}
        />
        <text
          className={chartStyles.axisLabel}
          x={chart.left - 8}
          y={chart.top + 4}
          textAnchor="end"
        >
          {ceiling}
        </text>
        <text
          className={chartStyles.axisLabel}
          x={chart.left - 8}
          y={chart.height - chart.bottom}
          textAnchor="end"
        >
          0
        </text>
        {labels.map((index) => (
          <text
            key={index}
            className={chartStyles.axisLabel}
            x={chart.left + index * step}
            y={chart.height - 8}
            textAnchor={index === 0 ? 'start' : index === count - 1 ? 'end' : 'middle'}
            data-testid="activity-x-label"
          >
            {report.days[index] ? labelFor(report.days[index].day, report.window) : ''}
          </text>
        ))}
        {partial !== null && (
          <text
            className={chartStyles.axisLabel}
            x={chart.width - chart.right}
            y={chart.top - 2}
            textAnchor="end"
            data-testid="activity-today-note"
          >
            {todayNote(partial.hours)}
          </text>
        )}
        {partial !== null && count > 1 && (
          <rect
            className={styles.todayBand}
            x={xAt(partial.index, count, chart) - step / 2}
            y={chart.top}
            width={step / 2}
            height={chart.height - chart.top - chart.bottom}
            data-testid="activity-today"
          />
        )}
        {view === 'kind'
          ? bands.map((band) => (
              <g
                key={band.kind}
                className={kindTone(band.kind)}
                data-testid={`activity-band-${band.kind}`}
              >
                <path className={styles.band} d={bandPath(band, ceiling, chart)} />
                <path className={styles.bandEdge} d={edgePath(band, ceiling, chart)} />
              </g>
            ))
          : lines.map((line) => (
              <g
                key={line.store}
                className={storeTone(line.store)}
                data-testid={`activity-line-${line.store}`}
              >
                <path className={styles.area} d={areaPath(line.points, ceiling, chart)} />
                <path className={chartStyles.line} d={linePath(line.points, ceiling, chart)} />
              </g>
            ))}
        {view === 'kind' &&
          bands.map((band) => {
            const spot = labelSpot(band, ceiling, chart);
            return spot === null ? null : (
              <text
                key={`label:${band.kind}`}
                className={styles.bandLabel}
                x={spot.x}
                y={spot.y}
                textAnchor={spot.anchor}
                data-testid={`activity-band-label-${band.kind}`}
              >
                {KIND_NAMES[band.kind]}
              </text>
            );
          })}
        {hovered !== null && (
          <g className={styles.crosshair} data-testid="activity-crosshair">
            <line
              x1={xAt(hovered, count, chart)}
              x2={xAt(hovered, count, chart)}
              y1={chart.top}
              y2={chart.height - chart.bottom}
            />
            {readings.map((reading) => (
              <circle
                key={reading.key}
                className={`${styles.crossDot} ${reading.className}`}
                cx={xAt(hovered, count, chart)}
                cy={yAt(reading.top, ceiling)}
                r={4}
              />
            ))}
          </g>
        )}
      </svg>
      {hovered !== null && (
        <ActivityReadout
          day={report.days[hovered].day}
          todayNote={
            partial !== null && partial.index === hovered ? todayNote(partial.hours) : null
          }
          readings={readings}
          total={
            view === 'kind'
              ? readings.reduce((sum, reading) => sum + reading.value, 0)
              : (readings[0]?.value ?? 0)
          }
          onLeft={xAt(hovered, count, chart) > chart.width / 2}
        />
      )}
    </div>
  );
}
