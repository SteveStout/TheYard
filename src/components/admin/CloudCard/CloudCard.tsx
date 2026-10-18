/**
 * The one frame every card under Cloud infrastructure is drawn in (ADR: The App Service plan, read every minute). A card reads its own route and hands the answer here; this file lays it out, and its
 * parts are in the folders beside it:
 *   CloudDial     the two dials, now, with the thirty-day 95th and peak marked
 *   CloudChart    the window's two series, deploys and events
 *   CloudRows     the four questions in the Overview card's row style
 *   CloudReadout  the readout beside the card
 */
import type { ReactNode } from 'react';
import { linePath, type CloudReport } from '../../../lib/cloud';
import type { Fetched } from '../shared/types';
import { CardState } from '../shared/CardState';
import cardStyles from '../shared/card.module.css';
import { CloudDial } from '../CloudDial';
import { CloudChart } from '../CloudChart';
import { CloudRows, PortalLink } from '../CloudRows';
import { CloudReadout } from '../CloudReadout';
import styles from './CloudCard.module.css';

/** One sixty-minute hairline in a bordered box, oldest on the left. */
function Spark({ name, values }: { name: string; values: (number | null)[] }) {
  const width = 100;
  const height = 30;
  const path = linePath(
    values.map((value, i) => ({
      x: values.length < 2 ? 0 : (i / (values.length - 1)) * width,
      y: value === null ? null : 3 + (height - 6) * (1 - Math.max(0, Math.min(100, value)) / 100),
    }))
  );
  return (
    <div className={styles.spark}>
      <span className={styles.sparkName}>{name}</span>
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

// #region cloud-frame
/**
 * The frame: a loading or failed line until the card's route answers, then the whole card. The
 * window buttons are the tab's shared ones, drawn beside the chart's title.
 */
export function CloudCard({
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
        {report === null ? (
          <CardState state="loading" />
        ) : (
          <p className={cardStyles.muted} data-testid="cloud-failed">
            Could not read this card on the last try; the next try is in a minute.
          </p>
        )}
      </article>
    );
  }
  const [left, right] = report.dials;
  return (
    <div className={styles.frame} data-testid={testId}>
      <article className={`${styles.card} op-glass`}>
        <header className={styles.header}>
          <h2 className={cardStyles.cardTitle}>{report.title}</h2>
          {report.spec !== null && <span className={styles.spec}>{report.spec}</span>}
        </header>
        {report.resource !== null && (
          <p className={styles.resource} data-testid="cloud-resource">
            <PortalLink link={report.resource} />{' '}
            <span className={styles.quiet}>
              opens in the Azure portal; your sign-in, not the site&rsquo;s
            </span>
          </p>
        )}
        {report.note !== null && (
          <p className={cardStyles.muted} data-testid="cloud-note">
            {report.note}
          </p>
        )}
        <div className={styles.dials}>
          {left !== undefined && <CloudDial dial={left} testId="cloud-dial-left" />}
          <div className={styles.between}>
            {report.sparks.map((spark) => (
              <Spark key={spark.name} name={spark.name} values={spark.values} />
            ))}
            <p className={styles.key}>
              <span className={styles.gold}>p95</span> <span className={styles.red}>peak</span> on
              dial
              <br />
              <span data-testid="cloud-read-age">
                {report.read_age_seconds === null
                  ? 'not read yet'
                  : `read ${report.read_age_seconds} s ago`}
              </span>
            </p>
          </div>
          {right !== undefined && <CloudDial dial={right} testId="cloud-dial-right" />}
        </div>
        <p className={styles.normal}>{report.normal}</p>
        <CloudChart chart={report.chart} endMs={report.as_of_ms} toolbar={toolbar} />
        <CloudRows rows={report.rows} />
      </article>
      <CloudReadout readout={report.readout} />
    </div>
  );
}
// #endregion cloud-frame
