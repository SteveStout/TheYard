/**
 * What Azure charges (ADR: What Azure charges): the three cards the portal's
 * subscription overview leads with, on the Admin tab in public. The spend so far
 * with Azure's forecast, what each resource cost, and how many resources of each
 * type are on the bill, over the window every chart on the tab shares.
 */
import type { ReactNode } from 'react';
import {
  costWindowFor,
  type CostReport,
  dayWords,
  donutArcs,
  headline,
  money,
  SPEND_BOX,
  spendLine,
  typeBars,
} from '../../../lib/spend';
import type { MachineWindow } from '../../../lib/machineChart';
import cardStyles from '../shared/card.module.css';
import chartStyles from '../charts/charts.module.css';
import styles from './SpendCard.module.css';
import { About, failed, useRead } from '../shared/common';
import { BarGauge } from '../charts';

const TONES = [styles.tone0, styles.tone1, styles.tone2, styles.tone3, styles.tone4];

export default function SpendCard({
  tick,
  window: chosen,
  toolbar,
}: {
  tick: number;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const costWindow = costWindowFor(chosen);
  const report = useRead<CostReport>(`/api/admin/costs?window=${costWindow}`, tick);
  return (
    <article className={`${cardStyles.wide} op-glass`} data-testid="spend-card">
      <h2 className={cardStyles.cardTitle}>What Azure charges</h2>
      <About>
        The bill for the subscription this site runs on, as Azure Cost Management reports it: the
        spend so far and what Azure forecasts for the rest of the month, what each resource cost,
        and how many resources of each type are on the bill. Azure reports a day eight to twenty
        four hours late, so the newest day is still being added to. The site reads the bill once an
        hour with its own identity and serves it from there, never asking Azure on your request.
      </About>
      {toolbar}
      {chosen === '1h' && (
        <p className={cardStyles.muted} data-testid="spend-hour-note">
          Azure bills by the day, so the last hour is shown as the last day.
        </p>
      )}
      {report === null ? (
        <p className={cardStyles.muted}>Loading…</p>
      ) : report === 'failed' ? (
        failed('what Azure charges')
      ) : !report.available ? (
        <p className={cardStyles.muted} data-testid="spend-note">
          Nothing to draw: {report.note}.
        </p>
      ) : (
        <SpendBody report={report} />
      )}
    </article>
  );
}

function SpendBody({ report }: { report: CostReport }) {
  const withForecast = report.window === '30d';
  const line = spendLine(report, withForecast);
  const arcs = donutArcs(report.resources);
  const bars = typeBars(report.types);
  const most = Math.max(0, ...bars.map((bar) => bar.resources));
  const [first, ...rest] = headline(report);
  return (
    <>
      <div className={styles.headline} data-testid="spend-headline">
        <span className={styles.figure}>{first}</span>
        {rest.map((sentence) => (
          <span key={sentence} className={cardStyles.muted}>
            {sentence}
          </span>
        ))}
      </div>
      {report.note !== null && (
        <p className={cardStyles.muted} data-testid="spend-stale">
          The last read did not go through, so this is the one before it: {report.note}.
        </p>
      )}
      <div className={styles.parts}>
        {/* #region spend-line-card */}
        <section className={`${styles.part} ${styles.whole}`} data-testid="spend-line">
          <h3 className={cardStyles.cardTitle}>Spend and forecast</h3>
          <p className={cardStyles.muted}>
            {money(report.window_total, report.currency)} in the window
            {withForecast ? ', and Azure’s forecast dashed to the month’s end' : ''}.
          </p>
          <svg
            className={chartStyles.chart}
            viewBox={`0 0 ${SPEND_BOX.width} ${SPEND_BOX.height}`}
            role="img"
            aria-label={`The running total over the window, reaching ${money(report.window_total, report.currency)} on ${report.newest_day === null ? 'the newest day' : dayWords(report.newest_day)}`}
          >
            <line
              className={chartStyles.axis}
              x1={SPEND_BOX.left}
              y1={SPEND_BOX.height - SPEND_BOX.bottom}
              x2={SPEND_BOX.width - SPEND_BOX.right}
              y2={SPEND_BOX.height - SPEND_BOX.bottom}
            />
            {line.ticks.map((tick) => (
              <text
                key={tick.label}
                className={chartStyles.axisLabel}
                x={SPEND_BOX.left - 6}
                y={tick.y + 4}
                textAnchor="end"
              >
                {tick.label}
              </text>
            ))}
            {line.ends.map((end, index) => (
              <text
                key={end.label}
                className={chartStyles.axisLabel}
                x={end.x}
                y={SPEND_BOX.height - 8}
                textAnchor={index === 0 && line.ends.length > 1 ? 'start' : 'end'}
              >
                {end.label}
              </text>
            ))}
            <path className={`${chartStyles.line} ${chartStyles.firstLine}`} d={line.actual} />
            {line.forecast !== '' && (
              <path
                className={`${chartStyles.line} ${chartStyles.firstLine} ${styles.forecastLine}`}
                d={line.forecast}
                data-testid="spend-forecast"
              />
            )}
            {line.newest !== null && (
              <circle
                className={styles.newest}
                cx={line.newest.x}
                cy={line.newest.y}
                r={4}
                data-testid={line.newest.partial ? 'spend-partial' : 'spend-newest'}
              />
            )}
          </svg>
        </section>
        {/* #endregion spend-line-card */}
        {/* #region spend-donut-card */}
        <section className={styles.part} data-testid="spend-resources">
          <h3 className={cardStyles.cardTitle}>Cost by resource</h3>
          <svg
            className={styles.donut}
            viewBox="0 0 160 160"
            role="img"
            aria-label={`What each resource cost in the window: ${report.resources.map((slice) => `${slice.name} ${slice.share}%`).join(', ')}`}
          >
            {arcs.map((arc) => (
              <path key={arc.name} className={`${styles.arc} ${TONES[arc.tone]}`} d={arc.path} />
            ))}
          </svg>
          <ul className={chartStyles.legend}>
            {report.resources.map((slice, index) => (
              <li key={slice.name} className={styles.legendRow} data-testid="spend-resource">
                <span className={styles.legendName}>
                  <span
                    className={`${chartStyles.swatch} ${TONES[slice.name === 'Others' ? 0 : index + 1]}`}
                  />
                  {slice.name === 'Others' ? `Others (${slice.count} resources)` : slice.name}
                </span>
                <span className={cardStyles.mono}>
                  {money(slice.cost, report.currency)} · {slice.share}%
                </span>
              </li>
            ))}
          </ul>
        </section>
        {/* #endregion spend-donut-card */}
        {/* #region spend-types-card */}
        <section className={styles.part} data-testid="spend-types">
          <h3 className={cardStyles.cardTitle}>Resources by type</h3>
          <p className={cardStyles.muted}>Every resource on the bill in the window, by type.</p>
          {bars.map((bar, index) => (
            <BarGauge
              key={bar.label}
              testId={`spend-type-${index}`}
              name={bar.label}
              ceiling={money(bar.cost, report.currency)}
              value={bar.resources}
              max={most}
              reading={`${bar.resources} resource${bar.resources === 1 ? '' : 's'}`}
            />
          ))}
        </section>
        {/* #endregion spend-types-card */}
      </div>
    </>
  );
}
