/**
 * Memory against traffic (ADR: Memory follows traffic), the last card under Cloud infrastructure:
 * it reads GET /api/admin/follows for the tab's window and plots the processor and the memory
 * against requests a minute. The processor should rise with the traffic and memory should stay
 * roughly flat; the server's rule reads memory that climbs while traffic is flat as red. The
 * arithmetic that places the dots is in src/lib/follows.ts; this file only draws.
 */
import type { ReactNode } from 'react';
import { followsWords, scatter, type FollowsReport, type Scatter } from '../../../lib/follows';
import type { MachineWindow } from '../../../lib/machineChart';
import { formatNumber } from '../../../lib/format';
import { CardState } from '../shared/CardState';
import { About, useRead } from '../shared/common';
import cardStyles from '../shared/card.module.css';
import styles from './FollowsCard.module.css';

const W = 300;
const H = 180;
const PAD = { left: 40, right: 10, top: 10, bottom: 30 };

/** One scatter: requests a minute along the bottom, the reading up the side, and the straight line through the dots. */
function Plot({
  title,
  plot,
  unit,
  testId,
  dotClass,
}: {
  title: string;
  plot: Scatter;
  unit: string;
  testId: string;
  dotClass: string;
}) {
  const innerW = W - PAD.left - PAD.right;
  const innerH = H - PAD.top - PAD.bottom;
  const x = (u: number) => PAD.left + u * innerW;
  const y = (u: number) => PAD.top + (1 - u) * innerH;
  return (
    <figure className={styles.plot} data-testid={testId}>
      <figcaption className={styles.plotTitle}>{title}</figcaption>
      <svg
        className={styles.svg}
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={`${title}: ${plot.dots.length} readings, requests a minute up to ${formatNumber(plot.xMax)}, ${unit} up to ${formatNumber(plot.yMax)}`}
      >
        <rect className={styles.frame} x={PAD.left} y={PAD.top} width={innerW} height={innerH} />
        {plot.dots.map((dot) => (
          <circle key={dot.at} className={dotClass} cx={x(dot.x)} cy={y(dot.y)} r={3}>
            <title>{`${dot.at}: ${formatNumber(dot.requests)} requests a minute, ${formatNumber(dot.value)} ${unit}`}</title>
          </circle>
        ))}
        {plot.trend && (
          <line
            className={styles.trend}
            x1={x(plot.trend.x1)}
            y1={y(plot.trend.y1)}
            x2={x(plot.trend.x2)}
            y2={y(plot.trend.y2)}
            data-testid={`${testId}-trend`}
          />
        )}
        <text className={styles.axisWord} x={PAD.left} y={H - 8}>
          0
        </text>
        <text className={styles.axisWord} x={W - PAD.right} y={H - 8} textAnchor="end">
          {formatNumber(plot.xMax)} requests a minute
        </text>
        <text className={styles.axisWord} x={PAD.left - 4} y={PAD.top + 8} textAnchor="end">
          {formatNumber(plot.yMax)}
        </text>
        <text className={styles.axisWord} x={PAD.left - 4} y={H - PAD.bottom} textAnchor="end">
          0
        </text>
      </svg>
    </figure>
  );
}

// #region follows-card
/**
 * The card: a loading or failed line until the route answers, then the verdict, the two plots and
 * the readout. The window buttons are the tab's shared ones.
 */
export default function FollowsCard({
  tick,
  window,
  toolbar,
}: {
  tick: number;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const report = useRead<FollowsReport>(`/api/admin/follows?window=${window}`, tick);
  return (
    <article className={`${cardStyles.wide} op-glass`} data-testid="follows-card">
      <h2 className={cardStyles.cardTitle}>Memory against traffic</h2>
      <About>
        The processor should rise and fall with the requests, and memory should stay roughly where
        it is whatever they do. Memory that keeps climbing while the traffic is flat is a leak or a
        cache with no bound, and this card reads it red: the second half of the window&rsquo;s
        middle working set at least 50 MB, or 5 per cent of the limit, above the first half&rsquo;s,
        the line through every point sloping up, and the requests a minute no more than a quarter
        higher. It would have flagged the hundred thousand vehicles each process held (ADR-101): in
        the week of 6 October the working set went from about 406 MB to 665 MB while traffic fell
        from 4.2 to 1.3 requests a minute, which this rule reads red on the kept readings; the plan
        card found it ten days later at 79 per cent of the plan. The hour leaves out a
        process&rsquo;s first fifteen minutes, when the runtime and a fill grow memory for reasons
        that are not a leak.
      </About>
      {toolbar}
      {report === null || report === 'failed' ? (
        report === null ? (
          <CardState state="loading" />
        ) : (
          <p className={cardStyles.muted} data-testid="follows-failed">
            Could not read memory against traffic on the last try.
          </p>
        )
      ) : (
        <FollowsBody report={report} />
      )}
    </article>
  );
}
// #endregion follows-card

/** The answer drawn: the verdict, the two plots, and what each number on them is. */
function FollowsBody({ report }: { report: FollowsReport }) {
  const verdict = report.verdict;
  const cpu = scatter(report.points, (point) => point.cpu_percent);
  const memory = scatter(report.points, (point) => point.working_set_mb, report.memory_limit_mb);
  const toneClass = styles[verdict.tone] ?? '';
  return (
    <>
      <p
        className={`${styles.verdict} ${toneClass}`}
        data-testid="follows-verdict"
        data-tone={verdict.tone}
      >
        {verdict.words}
      </p>
      <div className={styles.plots}>
        <Plot
          title="Processor, per cent, against requests a minute"
          plot={cpu}
          unit="per cent"
          testId="follows-cpu"
          dotClass={styles.dot}
        />
        <Plot
          title={`Memory, MB of ${formatNumber(report.memory_limit_mb)}, against requests a minute`}
          plot={memory}
          unit="MB"
          testId="follows-memory"
          dotClass={styles.memoryDot}
        />
      </div>
      <ul className={styles.readout} data-testid="follows-readout">
        <li>The processor {followsWords(verdict.cpu_follows_traffic)}.</li>
        <li>Memory {followsWords(verdict.memory_follows_traffic)}.</li>
        <li>
          Memory, first half to second half of the window: {formatNumber(verdict.memory_from_mb)} MB
          to {formatNumber(verdict.memory_to_mb)} MB; a climb is{' '}
          {formatNumber(verdict.climb_threshold_mb)} MB or more.
        </li>
        <li>
          Requests a minute, first half to second half: {formatNumber(verdict.requests_from)} to{' '}
          {formatNumber(verdict.requests_to)}.
        </li>
        <li>
          {report.points.length} readings,{' '}
          {report.bucket_minutes === 1 ? 'a minute' : `${report.bucket_minutes} minutes`} each
          {report.skipped_warm_up ? ", the process's first fifteen minutes left out" : ''}.
        </li>
      </ul>
    </>
  );
}
