/**
 * Does:      Draws every card under Cloud infrastructure from its report, in one frame (ADR: The onion cut into
 *            vertical slices, the CardShell; ADR: One frame for every resource): a lead block (the header, the sub
 *            line, the two dial panels as the approved canvas draws them, each with its name, its thresholds named
 *            on the dial, one quiet hold line and its sixty minutes) with the readout beside it, then the chart, the
 *            processes and the rows across the card's full width. The parts are in the folders beside it:
 *              CloudDial      a dial, now, each threshold named on it by a leader line
 *              CloudChart     the window, one panel a series against its own lines, the rolls on the time axis
 *              CloudRows      the four questions, each naming its source
 *              CloudReadout   the summary column, each line with its source under its label
 *              CloudProcesses the nightly card's description and its process sections, which no other card sends
 * Does not:  Read a route (each card reads its own and hands the answer here), or decide a figure, a tone or a source:
 *            every one comes from the server, and a slot the card has no data for is left out.
 * Used by:   src/components/admin/PlanCard/PlanCard.tsx, src/components/admin/CloudResourceCard/CloudResourceCard.tsx,
 *            through index.ts.
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
import { holdLine, sparksFor } from '../../../lib/cloudGauge';
import { CloudDial } from '../CloudDial';
import { CloudChart } from '../CloudChart';
import { CloudRows, PortalLink } from '../CloudRows';
import { CloudReadout } from '../CloudReadout';
import { CloudProcesses, CloudWhat } from '../CloudProcesses';
import areas from '../shared/cardAreas.module.css';
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

/**
 * A dial's panel as the approved canvas draws it: its name, the dial with its thresholds named on it, one quiet
 * line on how its colour is held, and its sixty minutes under it. A box inside the card rather than a panel of the
 * page, so it is a named group: the operator's look gives every section the glass, and a dial's box wears none.
 */
function DialPanel({
  dial,
  testId,
  hold,
  spark,
}: {
  dial: Dial;
  testId: string;
  hold: string;
  spark: { name: string; values: (number | null)[] } | undefined;
}) {
  return (
    <div className={styles.dialBox} role="group" aria-label={dial.name}>
      <h3 className={styles.dialName}>{dial.name}</h3>
      <CloudDial dial={dial} testId={testId} />
      {hold !== '' && <p className={styles.hold}>{hold}</p>}
      {spark !== undefined && <Spark name={spark.name} values={spark.values} tone={dial.tone} />}
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
 * order, the lead block beside the readout and everything below the dials across the card's full
 * width, as the approved layout draws it. The window buttons are the tab's shared ones, drawn
 * beside the chart's title.
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
  const hold = holdLine(report.thresholds);
  const sparks = sparksFor(report.dials.slice(0, 2), report.sparks);
  const instances = report.readout.lines.find((line) => line.key === 'instances');
  return (
    <div className={styles.frame} data-testid={testId} data-tone={report.tone}>
      <article className={`${areas.card} op-glass`}>
        <div className={areas.lead} data-testid="cloud-lead">
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
          {report.description != null && <CloudWhat description={report.description} />}
          {report.note !== null && (
            <p className={cardStyles.muted} data-testid="cloud-note">
              {report.note}
            </p>
          )}
          <div className={styles.dials}>
            {report.dials.slice(0, 2).map((dial, index) => (
              <DialPanel
                key={dial.name}
                dial={dial}
                testId={index === 0 ? 'cloud-dial-left' : 'cloud-dial-right'}
                hold={hold}
                spark={sparks.byDial[index]}
              />
            ))}
          </div>
          {sparks.rest.length > 0 && (
            <div className={styles.sparks}>
              {sparks.rest.map((spark) => (
                <Spark key={spark.name} name={spark.name} values={spark.values} tone="info" />
              ))}
            </div>
          )}
        </div>
        <div className={areas.readout}>
          <CloudReadout readout={report.readout} />
        </div>
        <div className={areas.rest} data-testid="cloud-rest">
          <CloudChart chart={report.chart} endMs={report.as_of_ms} toolbar={toolbar} />
          <CloudProcesses processes={report.processes ?? []} />
          <CloudRows rows={report.rows} />
        </div>
      </article>
    </div>
  );
}
// #endregion card-shell
