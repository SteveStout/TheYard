/**
 * Traffic (ADR: The Admin tab, as a product): how busy, how fast and how many
 * errors, over the window every chart on the tab shares, which the tab hands in.
 */
import type { ReactNode } from 'react';
import {
  fromFirstReading,
  keptTraffic,
  type MachineWindow,
  timeline,
  type TrafficSlot,
  trafficTotals,
  windowName,
} from '../../lib/machineChart';
import { TRAFFIC_CHARTS, failSentence, trafficBlocks } from '../../lib/trafficCard';
import styles from '../AdminPanel.module.css';
import type { Machines, Fetched } from './types';
import { About, hourSlots } from './common';
import { MachineChart, youngRecord } from './charts';

function TrafficCard({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Machines;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const history = machines.history;
  const wholeSlots =
    window_ !== '1h' && history !== undefined && history.window === window_ && history.available
      ? timeline(history.buckets, window_, history.bucket_minutes, new Date(history.as_of))
      : null;
  const keptSlots = wholeSlots === null ? null : fromFirstReading(wholeSlots);
  const slots: TrafficSlot[] | null =
    window_ === '1h' ? hourSlots(machines) : keptSlots === null ? null : keptTraffic(keptSlots);
  const minutesPerSlot = window_ === '1h' ? 1 : (history?.bucket_minutes ?? 1);
  const totals = slots === null ? null : trafficTotals(slots, minutesPerSlot);
  const series = (key: string, name: string, pick: (slot: TrafficSlot) => number | null) => ({
    key,
    name,
    points: (slots ?? []).map((slot) => ({ at: slot.at, value: pick(slot) })),
  });
  const stretch = windowName(window_).toLowerCase();
  const failed = totals === null ? null : failSentence(totals, stretch);
  // A block's tone is a state: good news, bad news, or neither. Never an identity.
  const blockTone = { good: styles.tileGood, bad: styles.tileBad, plain: styles.tilePlain };

  return (
    <article className={`${styles.wide} op-glass`} data-testid="traffic-card">
      <h2 className={styles.cardTitle}>Traffic</h2>
      <About>
        How busy the site is, how fast it is answering and whether anything is failing, over the
        window chosen here, which the machines card below follows. The last hour is the request ring
        this process keeps, {machines.traffic?.ring ?? 500} requests deep, a minute at a time; the
        wider windows are the minutes each site keeps in Azure Cosmos DB. This tab&rsquo;s own reads
        and the page sweep are not counted in either.
      </About>
      {toolbar}
      {slots === null || totals === null || failed === null ? (
        <p className={styles.muted} data-testid="traffic-note">
          {window_ === '1h'
            ? 'This build does not report its traffic a minute at a time.'
            : `${windowName(window_)} is not kept here: ${history?.note ?? 'the store did not answer'}`}
        </p>
      ) : (
        <>
          <ul
            className={styles.strip}
            aria-label={`Traffic over the ${stretch}, in four numbers`}
            data-testid="traffic-stats"
          >
            {trafficBlocks(slots, totals, stretch, window_ === '1h').map((block) => (
              <li key={block.key} className={styles.stripItem}>
                <div
                  className={`${styles.tile} op-glass op-tile ${styles.statBlock} ${blockTone[block.tone]}`}
                  data-testid={`traffic-stat-${block.key}`}
                  data-tone={block.tone}
                >
                  <span className={styles.tileLabel}>{block.label}</span>
                  <span className={styles.tileValue}>{block.value}</span>
                  <span className={styles.tileDetail}>{block.detail}</span>
                  {block.word !== null && <span className={styles.tileTone}>{block.word}</span>}
                </div>
              </li>
            ))}
          </ul>
          {keptSlots !== null && wholeSlots !== null && keptSlots.length < wholeSlots.length && (
            <p className={styles.muted} data-testid="traffic-young">
              {youngRecord(keptSlots)}.
            </p>
          )}
          <section className={styles.chartSection} data-testid="traffic-section-errors">
            <div className={styles.chartHead}>
              <span className={styles.indexChip} aria-hidden="true">
                01
              </span>
              <h3 className={styles.chartTitle}>{TRAFFIC_CHARTS.errors.title}</h3>
            </div>
            <p className={styles.chartRead}>{TRAFFIC_CHARTS.errors.read}</p>
            <p className={styles.statusRow} data-testid="traffic-fail-line">
              <span className={`${styles.pill} ${failed.good ? styles.ok : styles.bad}`}>
                {failed.good ? 'good' : 'needs attention'}
              </span>
              {failed.text}
            </p>
            <MachineChart
              testId="traffic-chart-errors"
              label={`Requests answered with an error over the ${stretch}, a minute: a server error is the site failing, a turned-away request is one it refused`}
              axisUnit={TRAFFIC_CHARTS.errors.unit}
              window={window_}
              tones={['bad', 'third']}
              series={[
                series('5xx', TRAFFIC_CHARTS.errors.server, (slot) => slot.server_errors),
                series('4xx', TRAFFIC_CHARTS.errors.turnedAway, (slot) => slot.client_errors),
              ]}
            />
          </section>
          <section className={styles.chartSection} data-testid="traffic-section-requests">
            <div className={styles.chartHead}>
              <span className={styles.indexChip} aria-hidden="true">
                02
              </span>
              <h3 className={styles.chartTitle}>{TRAFFIC_CHARTS.requests.title}</h3>
            </div>
            <p className={styles.chartRead}>{TRAFFIC_CHARTS.requests.read}</p>
            <MachineChart
              testId="traffic-chart-requests"
              label={`Requests a minute over the ${stretch}`}
              axisUnit={TRAFFIC_CHARTS.requests.unit}
              window={window_}
              tones={['first']}
              series={[
                series('requests', TRAFFIC_CHARTS.requests.requests, (slot) => slot.requests),
              ]}
            />
          </section>
          <section className={styles.chartSection} data-testid="traffic-section-timing">
            <div className={styles.chartHead}>
              <span className={styles.indexChip} aria-hidden="true">
                03
              </span>
              <h3 className={styles.chartTitle}>{TRAFFIC_CHARTS.timing.title}</h3>
            </div>
            <p className={styles.chartRead}>{TRAFFIC_CHARTS.timing.read}</p>
            <MachineChart
              testId="traffic-chart-timing"
              label={`How fast requests were answered over the ${stretch}: the median and the ninety-fifth, in milliseconds`}
              axisUnit={TRAFFIC_CHARTS.timing.unit}
              window={window_}
              tones={['first', 'second']}
              series={[
                series('p50', TRAFFIC_CHARTS.timing.typical, (slot) => slot.p50_ms),
                series('p95', TRAFFIC_CHARTS.timing.slow, (slot) => slot.p95_ms),
              ]}
            />
          </section>
        </>
      )}
    </article>
  );
}

export default function TrafficSection({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Fetched<Machines>;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  if (machines === null || machines === 'failed') {
    return (
      <article className={`${styles.wide} op-glass`} data-testid="traffic-card">
        <h2 className={styles.cardTitle}>Traffic</h2>
        {toolbar}
        <p className={styles.muted}>
          {machines === null ? 'Loading…' : 'Could not read the traffic on the last try.'}
        </p>
      </article>
    );
  }
  return <TrafficCard machines={machines} window={window_} toolbar={toolbar} />;
}
