/**
 * A Cloud infrastructure card's window as one chart (ADR: The App Service plan, read every minute): the
 * first series in teal and the second in dark green against the dashed red line, deploys as gold
 * lines with a triangle on top, events as red dotted lines, and the days named on Mondays and
 * Fridays. A missing bucket breaks the line rather than drawing a zero nobody measured.
 */
import type { ReactNode } from 'react';
import { dayMarks, linePath, xOf, yOf, type CloudReport } from '../../../lib/cloud';
import styles from './CloudChart.module.css';

const W = 560;
const H = 150;
const LEFT = 34;
const RIGHT = 4;
const TOP = 6;
const PLOT = 112;

export function CloudChart({
  chart,
  endMs,
  toolbar,
}: {
  chart: CloudReport['chart'];
  endMs: number;
  toolbar: ReactNode;
}) {
  const x = (at: number) => LEFT + xOf(at, chart.start_ms, endMs, W - LEFT - RIGHT);
  const y = (percent: number) => TOP + yOf(percent, PLOT);
  const [first, second] = chart.series;
  const line = (series: typeof first | undefined) =>
    series === undefined
      ? ''
      : linePath(
          series.points.map((p) => ({ x: x(p.at_ms), y: p.value === null ? null : y(p.value) }))
        );
  return (
    <figure className={styles.chart} data-testid="cloud-chart">
      <div className={styles.head}>
        <span className={styles.title}>{chart.title}</span>
        {toolbar}
      </div>
      <svg
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={`${chart.title}, with deploys and events marked`}
      >
        <rect className={styles.frame} x={LEFT} y={TOP} width={W - LEFT - RIGHT} height={PLOT} />
        {[20, 40, 60, 80].map((level) => (
          <g key={level}>
            <line className={styles.grid} x1={LEFT} x2={W - RIGHT} y1={y(level)} y2={y(level)} />
            <text className={styles.axis} x={LEFT - 5} y={y(level) + 3} textAnchor="end">
              {level}%
            </text>
          </g>
        ))}
        {/* #region deploy-bands */}
        {/* Each roll shaded behind the lines, so a spike inside one reads as the deploy it was. */}
        {chart.bands.map((band) => (
          <rect
            key={`${band.start_ms}-${band.title}`}
            className={styles.band}
            x={x(band.start_ms)}
            y={TOP}
            width={Math.max(2, x(band.end_ms) - x(band.start_ms))}
            height={PLOT}
            data-testid="cloud-band"
          >
            <title>{band.title}</title>
          </rect>
        ))}
        {/* #endregion deploy-bands */}
        <line
          className={styles.redLine}
          x1={LEFT}
          x2={W - RIGHT}
          y1={y(chart.red_line)}
          y2={y(chart.red_line)}
        />
        {/* #region plan-markers */}
        {/* A gold line where the build changed, a red dashed line where Azure acted, on the series' own axis. */}
        {chart.marks.map((mark) =>
          mark.kind === 'deploy' ? (
            <g key={`${mark.at_ms}-${mark.title}`} className={styles.deploy}>
              <line x1={x(mark.at_ms)} x2={x(mark.at_ms)} y1={TOP} y2={TOP + PLOT} />
              <path
                d={`M ${x(mark.at_ms) - 3.5} ${TOP - 4} L ${x(mark.at_ms) + 3.5} ${TOP - 4} L ${x(mark.at_ms)} ${TOP + 2} Z`}
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
              y2={TOP + PLOT}
            >
              <title>{mark.title}</title>
            </line>
          )
        )}
        {/* #endregion plan-markers */}
        <path className={styles.second} d={line(second)} />
        <path className={styles.first} d={line(first)} />
        {dayMarks(chart.start_ms, endMs)
          .filter((mark) => mark.label !== null)
          .map((mark) => (
            <text
              key={mark.atMs}
              className={mark.label === 'Mon' ? styles.monday : styles.axis}
              x={x(mark.atMs)}
              y={H - 18}
              textAnchor="middle"
            >
              {`${mark.label} ${new Date(mark.atMs).getUTCDate()}`}
            </text>
          ))}
      </svg>
      <figcaption className={styles.legend}>{chart.legend}</figcaption>
    </figure>
  );
}
