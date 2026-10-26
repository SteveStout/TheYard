/**
 * Does:      Draws one panel of a card's chart (ADR: One frame for every resource, the addendum on how a card draws
 *            time): its own amber and red lines labelled in the left margin, light day lines, each roll's span shaded
 *            at an hour or a day, Azure's events and (at an hour or a day) each roll at full height, the peak band at
 *            seven and thirty days, and the series last and heaviest, so the reading is the darkest thing on it.
 * Does not:  Decide which panels a chart has, which marks are lines or where a label goes (src/lib/cloudChart.ts
 *            does), or draw the time axis and the legend (CloudChart.tsx does).
 * Used by:   src/components/admin/CloudChart/CloudChart.tsx.
 */
import { linePath, yOf } from '../../../lib/cloud';
import {
  bandsShown,
  fullLines,
  isWide,
  lineLabel,
  midnights,
  peakBand,
  type Chart,
  type Panel,
} from '../../../lib/cloudChart';
import styles from './CloudChart.module.css';

export const LEFT = 58;
export const RIGHT = 8;
const TOP = 6;
const BOTTOM = 4;

/** The class a series is drawn in, by its place in the chart: teal first, dark green second, then the quieter two. */
const SERIES = ['first', 'second', 'third', 'fourth'] as const;

export function CloudPanel({
  chart,
  panel,
  window,
  width,
  height,
  x,
  endMs,
}: {
  chart: Chart;
  panel: Panel;
  window: string;
  width: number;
  height: number;
  x: (atMs: number) => number;
  endMs: number;
}) {
  const plot = height - TOP - BOTTOM;
  const y = (percent: number) => TOP + yOf(percent, plot);
  const wide = isWide(window);
  const lines = [
    ...(panel.amber === null ? [] : [{ kind: 'amber' as const, level: panel.amber }]),
    ...(panel.red === null ? [] : [{ kind: 'red' as const, level: panel.red }]),
  ].filter((line) => line.level <= 100);
  return (
    <svg
      className={styles.panel}
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={`${panel.name}${lines.map((l) => `, ${lineLabel(l.kind, l.level)} %`).join('')}`}
      data-testid="cloud-panel"
    >
      <rect className={styles.frame} x={LEFT} y={TOP} width={width - LEFT - RIGHT} height={plot} />
      {midnights(chart.start_ms, endMs).map((at) => (
        <line key={at} className={styles.grid} x1={x(at)} x2={x(at)} y1={TOP} y2={TOP + plot} />
      ))}
      <text className={styles.axis} x={LEFT - 6} y={TOP + plot} textAnchor="end">
        0 %
      </text>
      {/* #region deploy-bands */}
      {/* Each roll shaded behind the lines at an hour or a day, so a spike inside one reads as the deploy it was. */}
      {bandsShown(window) &&
        chart.bands.map((band) => (
          <rect
            key={`${band.start_ms}-${band.title}`}
            className={styles.band}
            x={x(band.start_ms)}
            y={TOP}
            width={Math.max(2, x(band.end_ms) - x(band.start_ms))}
            height={plot}
            data-testid="cloud-band"
          >
            <title>{band.title}</title>
          </rect>
        ))}
      {/* #endregion deploy-bands */}
      {lines.map((line) => (
        <g key={line.kind} data-testid="cloud-line-label">
          <line
            className={line.kind === 'red' ? styles.redLine : styles.amberLine}
            x1={LEFT}
            x2={width - RIGHT}
            y1={y(line.level)}
            y2={y(line.level)}
          />
          <text
            className={line.kind === 'red' ? styles.redText : styles.amberText}
            x={LEFT - 6}
            y={y(line.level) + 3}
            textAnchor="end"
          >
            {lineLabel(line.kind, line.level)}
          </text>
        </g>
      ))}
      {/* #region plan-markers */}
      {/* What Azure did as a red dotted line at full height; a roll as a gold line with a triangle, at an hour or a day. */}
      {fullLines(chart.marks, window).map((mark) =>
        mark.kind === 'deploy' ? (
          <g key={`${mark.at_ms}-${mark.title}`} className={styles.deploy}>
            <line x1={x(mark.at_ms)} x2={x(mark.at_ms)} y1={TOP} y2={TOP + plot} />
            <path
              d={`M ${x(mark.at_ms) - 3.5} ${TOP} L ${x(mark.at_ms) + 3.5} ${TOP} L ${x(mark.at_ms)} ${TOP + 6} Z`}
            />
            <title>{mark.title}</title>
          </g>
        ) : (
          <line
            key={`${mark.at_ms}-${mark.title}`}
            className={styles.event}
            x1={x(mark.at_ms)}
            x2={x(mark.at_ms)}
            y1={TOP}
            y2={TOP + plot}
            data-testid="cloud-event"
          >
            <title>{mark.title}</title>
          </line>
        )
      )}
      {/* #endregion plan-markers */}
      {panel.series.map((series) => {
        const at = Math.max(0, chart.series.indexOf(series));
        const tone = styles[SERIES[at % SERIES.length]];
        return (
          <g key={series.name}>
            {wide && (
              <path className={`${styles.peakBand} ${tone}`} d={peakBand(series.points, x, y)} />
            )}
            <path
              className={`${styles.line} ${tone}`}
              d={linePath(
                series.points.map((p) => ({
                  x: x(p.at_ms),
                  y: p.value === null ? null : y(p.value),
                }))
              )}
            />
          </g>
        );
      })}
    </svg>
  );
}
