/**
 * The one component that draws every card under Cloud infrastructure from its CardView (ADR: The
 * onion cut into vertical slices, the CardShell; ADR: One frame for every resource). A card reads
 * its own route and hands the answer here; this file lays the slots out, and the parts are in the
 * folders beside it:
 *   CloudDial     the dials, now, with the amber and red ticks and the thirty-day 95th marked
 *   CloudChart    the window's two series, one gold band per roll, the legend as chips
 *   CloudRows     the four questions, each naming its source
 *   CloudReadout  the summary column, each line with its source under its label
 * Every figure, tone and source comes from the server; a slot the card has no data for is left out.
 */
import { type ReactNode, useEffect, useState } from 'react';
import {
  cardToneWord,
  linePath,
  newestValue,
  portalSentence,
  type CloudDial as Dial,
  type CloudReport,
} from '../../../lib/cloud';
import type { Fetched } from '../shared/types';
import { CardState } from '../shared/CardState';
import { REFRESH_MS } from '../shared/common';
import cardStyles from '../shared/card.module.css';
import { CloudDial } from '../CloudDial';
import { CloudChart } from '../CloudChart';
import { CloudRows, PortalLink } from '../CloudRows';
import { CloudReadout } from '../CloudReadout';
import styles from './CardShell.module.css';

/** One sixty-minute hairline in a bordered box, oldest on the left, in its dial's tone, with the newest figure on its title line. */
function Spark({ name, values, tone }: { name: string; values: (number | null)[]; tone: string }) {
  const width = 100;
  const height = 30;
  const path = linePath(
    values.map((value, i) => ({
      x: values.length < 2 ? 0 : (i / (values.length - 1)) * width,
      y: value === null ? null : 3 + (height - 6) * (1 - Math.max(0, Math.min(100, value)) / 100),
    }))
  );
  const newest = newestValue(values);
  return (
    <div className={styles.spark} data-tone={tone} data-testid="cloud-spark">
      <span className={styles.sparkName}>
        <span>{name}</span>
        <span className={styles.sparkNow}>{newest === null ? '' : `${Math.round(newest)} %`}</span>
      </span>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={name}
      >
        <path d={path} />
      </svg>
    </div>
  );
}

/** A dial and the line under it that names its marks: the 95th, the amber and the red. */
function DialWithMarks({ dial, testId }: { dial: Dial; testId: string }) {
  return (
    <div className={styles.dialBox}>
      <CloudDial dial={dial} testId={testId} />
      <p className={styles.marks}>
        {dial.p95 === null ? '' : `p95 ${Math.round(dial.p95)} % marked · `}amber {dial.amber} · red{' '}
        {dial.red}
      </p>
    </div>
  );
}

/** Seconds until the tab's next read, counted down from REFRESH_MS, for the line a failed read prints. */
function useNextTry(): number {
  const [left, setLeft] = useState(REFRESH_MS / 1000);
  useEffect(() => {
    const started = Date.now();
    const timer = window.setInterval(() => {
      const passed = Math.floor((Date.now() - started) / 1000);
      setLeft(Math.max(0, REFRESH_MS / 1000 - (passed % (REFRESH_MS / 1000))));
    }, 1000);
    return () => window.clearInterval(timer);
  }, []);
  return left;
}

/** The line a card prints when its last read failed, with the seconds to the next. */
function Retry() {
  const left = useNextTry();
  return (
    <p className={cardStyles.muted} data-testid="cloud-failed">
      Could not read this card on the last try; the next try is in {left} s.
    </p>
  );
}

// #region card-shell
/**
 * The shell: a loading or failed line until the card's route answers, then the slots in mockup 6's
 * order. The window buttons are the tab's shared ones, drawn beside the chart's title.
 */
export function CardShell({
  report,
  title,
  testId,
  toolbar,
}: {
  report: Fetched<CloudReport>;
  title: string;
  testId: string;
  toolbar: ReactNode;
}) {
  if (report === null || report === 'failed') {
    return (
      <article className={`${cardStyles.wide} op-glass`} data-testid={testId}>
        <h2 className={cardStyles.cardTitle}>{title}</h2>
        {report === null ? <CardState state="loading" /> : <Retry />}
      </article>
    );
  }
  const [left, right] = report.dials;
  const instances = report.readout.lines.find((line) => line.key === 'instances');
  return (
    <div className={styles.frame} data-testid={testId} data-tone={report.tone}>
      <article className={`${styles.card} op-glass`}>
        <header className={styles.header}>
          <h2 className={`${cardStyles.cardTitle} ${styles.title}`}>
            {report.title}
            <span className={styles.toneChip} data-tone={report.tone} data-testid="card-tone">
              {cardToneWord(report.tone)}
            </span>
          </h2>
          {report.spec !== null && (
            <span className={styles.spec}>
              {report.spec}
              {report.spec_source !== null && (
                <span className={styles.sourceChip} data-testid="card-spec-source">
                  {report.spec_source}
                </span>
              )}
            </span>
          )}
        </header>
        <p className={styles.resource} data-testid="cloud-resource">
          {report.resource !== null && (
            <>
              <PortalLink link={report.resource} />{' '}
              <span className={styles.quiet}>{portalSentence(report.resource.href)}</span>
            </>
          )}
          <span className={styles.quiet} data-testid="cloud-read-age">
            {' · '}
            {report.read_age_seconds === null
              ? 'not read yet'
              : `read ${report.read_age_seconds} s ago`}
          </span>
          {instances !== undefined && !instances.value.startsWith('not read') && (
            <span className={styles.quiet}>
              {' · '}
              {instances.value} instances
            </span>
          )}
        </p>
        {report.note !== null && (
          <p className={cardStyles.muted} data-testid="cloud-note">
            {report.note}
          </p>
        )}
        <div className={styles.dials}>
          {left !== undefined && <DialWithMarks dial={left} testId="cloud-dial-left" />}
          <div className={styles.between}>
            {report.sparks.map((spark, index) => (
              <Spark
                key={spark.name}
                name={spark.name}
                values={spark.values}
                tone={report.dials[index]?.tone ?? 'info'}
              />
            ))}
          </div>
          {right !== undefined && <DialWithMarks dial={right} testId="cloud-dial-right" />}
        </div>
        <ul className={styles.chips} aria-label="Thresholds" data-testid="card-thresholds">
          {report.thresholds.map((chip) => (
            <li key={chip.label} className={styles.chip} data-tone={chip.tone}>
              {chip.label}
            </li>
          ))}
        </ul>
        <CloudChart chart={report.chart} endMs={report.as_of_ms} toolbar={toolbar} />
        <CloudRows rows={report.rows} />
      </article>
      <CloudReadout readout={report.readout} />
    </div>
  );
}
// #endregion card-shell
