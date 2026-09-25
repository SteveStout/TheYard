/**
 * What the machines are doing (ADR: What the machines are doing), over the window
 * every chart on the tab shares, which the tab hands in.
 */
import type { ReactNode } from 'react';
import {
  busiestRate,
  coverage,
  fromFirstReading,
  type MachineWindow,
  requestUnitsAMinute,
  shareOf,
  timeline,
  windowName,
} from '../../lib/machineChart';
import styles from '../AdminPanel.module.css';
import type { Machines, Fetched } from './types';
import { About } from './common';
import { BarGauge, MachineChart, youngRecord } from './charts';

export default function MachinesCard({
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
      <article className={`${styles.wide} op-glass`} data-testid="machines-card">
        <h2 className={styles.cardTitle}>What the machines are doing</h2>
        {toolbar}
        {machines === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : (
          <p className={styles.muted} data-testid="machines-failed">
            Could not read the machines on the last try.
          </p>
        )}
      </article>
    );
  }
  return <MachinesBody machines={machines} window={window_} toolbar={toolbar} />;
}

function MachinesBody({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Machines;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const samples = machines.container.samples;
  const latest = samples.length > 0 ? samples[samples.length - 1] : null;
  const peak = samples.reduce((most, sample) => Math.max(most, sample.working_set_mb), 0);
  const newest = machines.relational.rows.length > 0 ? machines.relational.rows[0] : null;
  const worst = machines.relational.rows.reduce(
    (most, row) => Math.max(most, row.cpu_percent, row.data_io_percent, row.log_write_percent),
    0
  );

  return (
    <article className={`${styles.wide} op-glass`} data-testid="machines-card">
      <h2 className={styles.cardTitle}>What the machines are doing</h2>
      <About>
        The three machines under this site, each reporting the way it actually reports. The
        container knows its own memory and its own processor time, and the limit its share is read
        against is the runtime&rsquo;s own, which is lower than the memory the machine has, and a
        process that passes its own limit is the one that gets collected; Azure SQL Database keeps a
        reading of itself for the last hour, fifteen seconds at a time, free on every tier; Azure
        Cosmos DB has no memory or processor reading to give, because it is sold by request unit, so
        what it shows is what the operations cost against the free allowance. The last hour is this
        container&rsquo;s own, kept in its memory, and empties on every roll. The wider windows are
        a minute at a time, kept in Azure Cosmos DB by each site for thirty-one days, so they
        survive a roll and show one.
      </About>
      {toolbar}
      <p className={styles.muted} data-testid="machines-window-line">
        Showing {windowName(window_).toLowerCase()}. One window for every chart on this tab: the
        buttons here, on the traffic card and over the tiles are the same buttons.
      </p>
      {window_ !== '1h' && <KeptWindow machines={machines} window={window_} />}

      <h3 className={styles.cardTitle}>
        The container{window_ === '1h' ? '' : ', as this process remembers the last hour'}
      </h3>
      {latest === null ? (
        <p className={styles.muted} data-testid="machines-no-samples">
          No sample yet. One is taken every {machines.container.every_seconds} seconds.
        </p>
      ) : (
        <>
          <p data-testid="machines-container-line">
            <strong>
              {latest.working_set_mb} MB of {machines.container.memory_limit_mb} MB
            </strong>{' '}
            in use, {latest.managed_mb} MB of it managed objects,{' '}
            {latest.cpu_percent === null
              ? 'processor share not read yet'
              : `${latest.cpu_percent}% of ${machines.container.processors} processor${machines.container.processors === 1 ? '' : 's'}`}
            , {latest.threads} threads, {latest.gen0_collections} quick collections and{' '}
            {latest.gen2_collections} full ones since this container started. Peak working set in
            the window: {peak} MB.
          </p>
          <BarGauge
            testId="machines-memory-gauge"
            name="Memory"
            ceiling={`${machines.container.memory_limit_mb.toLocaleString()} MB`}
            value={latest.working_set_mb}
            max={machines.container.memory_limit_mb}
            reading={`${Math.round((latest.working_set_mb / Math.max(1, machines.container.memory_limit_mb)) * 100)} % · ${latest.working_set_mb.toLocaleString()} MB`}
          />
          {machines.container.catalogues && machines.container.catalogues.length > 0 && (
            // Most of that memory is catalogues, a hundred thousand vehicles a
            // store, so the card says which ones the process is holding. The
            // store this site does not serve is loaded on demand and let go
            // when nobody has asked for it in a while (ADR: One plan, two sites).
            <p className={styles.muted} data-testid="machines-catalogues">
              Catalogues in memory:{' '}
              {machines.container.catalogues
                .map(
                  (catalogue) =>
                    `${catalogue.store}, ${catalogue.serves ? 'which this site serves' : 'loaded on demand'}, ${catalogue.loaded ? 'held' : 'not held'}`
                )
                .join('; ')}
              .
            </p>
          )}
          <MachineChart
            testId="machine-chart-container"
            label="The container over the sampled window: memory as a share of its limit, and processor share"
            percentage
            series={[
              {
                key: 'memory',
                name: `Memory, share of ${machines.container.memory_limit_mb} MB`,
                points: samples.map((sample) => ({
                  at: sample.at,
                  value:
                    machines.container.memory_limit_mb > 0
                      ? Math.round(
                          (sample.working_set_mb / machines.container.memory_limit_mb) * 1000
                        ) / 10
                      : null,
                })),
              },
              {
                key: 'cpu',
                name: `Processor, share of ${machines.container.processors}`,
                points: samples.map((sample) => ({ at: sample.at, value: sample.cpu_percent })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="The container, sampled"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-container-table">
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col" className={styles.num}>
                    Working set
                  </th>
                  <th scope="col" className={styles.num}>
                    Managed
                  </th>
                  <th scope="col" className={styles.num}>
                    Heap
                  </th>
                  <th scope="col" className={styles.num}>
                    Processors
                  </th>
                  <th scope="col" className={styles.num}>
                    Threads
                  </th>
                </tr>
              </thead>
              <tbody>
                {[...samples]
                  .reverse()
                  .slice(0, 20)
                  .map((sample) => (
                    <tr key={sample.at}>
                      <td className={styles.mono}>{new Date(sample.at).toLocaleTimeString()}</td>
                      <td className={`${styles.mono} ${styles.num}`}>{sample.working_set_mb} MB</td>
                      <td className={`${styles.mono} ${styles.num}`}>{sample.managed_mb} MB</td>
                      <td className={`${styles.mono} ${styles.num}`}>{sample.heap_mb} MB</td>
                      <td className={`${styles.mono} ${styles.num}`}>
                        {sample.cpu_percent === null ? 'first' : `${sample.cpu_percent}%`}
                      </td>
                      <td className={`${styles.mono} ${styles.num}`}>{sample.threads}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <h3 className={styles.cardTitle}>{machines.relational.store}</h3>
      {!machines.relational.available ? (
        <p className={styles.muted} data-testid="machines-relational-note">
          {machines.relational.note}
        </p>
      ) : (
        <>
          <p data-testid="machines-relational-line">
            {newest === null ? (
              'The view answered with no rows yet.'
            ) : (
              <>
                <strong>
                  {newest.cpu_percent}% processor, {newest.memory_percent}% memory
                </strong>{' '}
                in the last fifteen seconds, {newest.data_io_percent}% data and{' '}
                {newest.log_write_percent}% log, {newest.worker_percent}% of the workers the tier
                allows. Busiest reading in the window: {worst}%. Every figure is a share of what
                this tier allows, which on Basic is five DTUs.
              </>
            )}
          </p>
          <MachineChart
            testId="machine-chart-relational"
            label="The relational store over the last hour, as it reports itself: processor and memory as shares of what the tier allows"
            percentage
            series={[
              {
                key: 'memory',
                name: 'Memory, share of the tier',
                points: [...machines.relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.memory_percent })),
              },
              {
                key: 'cpu',
                name: 'Processor, share of the tier',
                points: [...machines.relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.cpu_percent })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="The relational store's own reading"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-relational-table">
              <thead>
                <tr>
                  <th scope="col">At</th>
                  <th scope="col">Processor</th>
                  <th scope="col">Memory</th>
                  <th scope="col">Data</th>
                  <th scope="col">Log</th>
                  <th scope="col">Workers</th>
                </tr>
              </thead>
              <tbody>
                {machines.relational.rows.slice(0, 20).map((row) => (
                  <tr key={row.at}>
                    <td className={styles.mono}>{new Date(row.at).toLocaleTimeString()}</td>
                    <td className={styles.mono}>{row.cpu_percent}%</td>
                    <td className={styles.mono}>{row.memory_percent}%</td>
                    <td className={styles.mono}>{row.data_io_percent}%</td>
                    <td className={styles.mono}>{row.log_write_percent}%</td>
                    <td className={styles.mono}>{row.worker_percent}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <h3 className={styles.cardTitle}>{machines.document.store}</h3>
      {!machines.document.available ? (
        <p className={styles.muted} data-testid="machines-document-note">
          {machines.document.note}
        </p>
      ) : (
        <>
          <p data-testid="machines-document-line">
            <strong>{machines.document.request_units} request units</strong> across{' '}
            {machines.document.operations} operations in the ring, {machines.document.p50_ms} ms at
            the median and {machines.document.p95_ms} ms at the ninety-fifth. The free tier allows{' '}
            {machines.document.free_request_units_per_second} request units a second, and the gauge
            below is the busiest minute in the ring as a rate against that allowance. There is no
            memory or processor reading here: the store is sold by request unit and reports neither.
          </p>
          <BarGauge
            testId="machines-ru-gauge"
            name="Request units, busiest minute"
            ceiling={`${machines.document.free_request_units_per_second.toLocaleString()} / s free`}
            value={busiestRate(machines.document.minutes)}
            max={machines.document.free_request_units_per_second}
            reading={`${busiestRate(machines.document.minutes).toLocaleString()} / s`}
            tone="gold"
          />
          <MachineChart
            testId="machine-chart-document"
            label="What the document store charged, request units a minute"
            unit="request units a minute"
            series={[
              {
                key: 'ru',
                name: 'Request units a minute',
                points: machines.document.minutes.map((minute) => ({
                  at: minute.at,
                  value: minute.request_units,
                })),
              },
            ]}
          />
          <div
            className={styles.tableWrap}
            role="region"
            aria-label="What the document store charged"
            tabIndex={0}
          >
            <table className={styles.table} data-testid="machines-document-table">
              <thead>
                <tr>
                  <th scope="col">Minute</th>
                  <th scope="col">Request units</th>
                  <th scope="col">Operations</th>
                  <th scope="col">Share of a free second</th>
                </tr>
              </thead>
              <tbody>
                {[...machines.document.minutes]
                  .reverse()
                  .slice(0, 20)
                  .map((minute) => (
                    <tr key={minute.at}>
                      <td className={styles.mono}>{new Date(minute.at).toLocaleTimeString()}</td>
                      <td className={styles.mono}>{minute.request_units} RU</td>
                      <td className={styles.mono}>{minute.operations}</td>
                      <td className={styles.mono}>{minute.share_of_free_percent}%</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </article>
  );
}

// #region kept-window
/**
 * A day, a week or a month of the machines, from the minutes each site keeps in
 * the document store (ADR: What the machines are doing, the addendum on the
 * windows). Three charts in the units the hour uses, drawn over the whole
 * window slot by slot, so a stretch the store holds nothing for is a gap in
 * the line and not a line drawn across it. A window the store cannot answer
 * says why instead of drawing an empty chart.
 */
function KeptWindow({ machines, window: kept }: { machines: Machines; window: MachineWindow }) {
  const history = machines.history;
  if (kept === '1h' || history === undefined || history.window !== kept) {
    return null;
  }
  if (!history.available) {
    return (
      <p className={styles.muted} data-testid="machines-history-note">
        {windowName(kept)} is not kept here: {history.note}
      </p>
    );
  }

  const whole = timeline(history.buckets, kept, history.bucket_minutes, new Date(history.as_of));
  const held = coverage(whole);
  // Counted against the whole window, drawn from the first reading.
  const slots = fromFirstReading(whole);
  const grain =
    history.bucket_minutes >= 60
      ? `${history.bucket_minutes / 60}-hour`
      : `${history.bucket_minutes}-minute`;
  const peak = history.buckets.reduce(
    (most, bucket) => Math.max(most, bucket.working_set_max_mb),
    0
  );
  const charged = history.buckets.reduce((sum, bucket) => sum + bucket.request_units, 0);

  return (
    <div data-testid="machines-history">
      <p data-testid="machines-history-line">
        <strong>{windowName(kept)}</strong>, in {grain} buckets, from the minutes the{' '}
        {history.site === 'cosmos' ? 'Cosmos DB' : 'SQL'} site has kept: {held.held} of {held.of}{' '}
        buckets hold a reading.{' '}
        {slots.length < whole.length
          ? `${youngRecord(slots)}; a gap after the first reading is drawn as the gap it is.`
          : 'A stretch with no reading is drawn as the gap it is.'}
        {held.held > 0 &&
          ` Peak working set in the window: ${peak} MB. The document store charged ${Math.round(charged * 100) / 100} request units in it.`}
        {history.note !== null && held.held === 0 && ` ${history.note}.`}
      </p>
      <MachineChart
        testId="machine-history-container"
        label={`The container over the ${windowName(kept).toLowerCase()}: memory as a share of its limit, and processor share`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Memory, share of the limit',
            points: slots.map((slot) => ({
              at: slot.at,
              value:
                slot.bucket === null
                  ? null
                  : shareOf(slot.bucket.working_set_mb, slot.bucket.memory_limit_mb),
            })),
          },
          {
            key: 'cpu',
            name: 'Processor share',
            points: slots.map((slot) => ({ at: slot.at, value: slot.bucket?.cpu_percent ?? null })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-relational"
        label={`The relational store over the ${windowName(kept).toLowerCase()}, as it reported itself each minute: processor and memory as shares of what the tier allows`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Relational store: memory, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_memory_percent ?? null,
            })),
          },
          {
            key: 'cpu',
            name: 'Relational store: processor, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_cpu_percent ?? null,
            })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-document"
        label={`What the document store charged over the ${windowName(kept).toLowerCase()}, request units a minute`}
        unit="request units a minute"
        window={kept}
        series={[
          {
            key: 'ru',
            name: 'Document store',
            points: slots.map((slot) => ({ at: slot.at, value: requestUnitsAMinute(slot.bucket) })),
          },
        ]}
      />
    </div>
  );
}
// #endregion kept-window
