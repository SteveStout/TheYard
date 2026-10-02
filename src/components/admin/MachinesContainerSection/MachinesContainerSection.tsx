/**
 * The container's part of the machines card: its memory against the runtime's
 * own limit, its processor share, the catalogues it holds, a chart of the
 * sampled window and the newest twenty samples. Its own file because the
 * container reports differently from either store and reads on its own.
 */
import styles from '../shared/card.module.css';
import type { MachineSample, Machines } from '../shared/types';
import type { MachineWindow } from '../../../lib/machineChart';
import { BarGauge, MachineChart } from '../charts';
import { type Column, DataTable } from '../DataTable';
import { formatNumber } from '../../../lib/format';

/** The container every fifteen seconds: memory three ways, the processor and the threads. */
const CONTAINER_COLUMNS: Column<MachineSample>[] = [
  {
    name: 'At',
    mono: true,
    short: true,
    cell: (sample) => new Date(sample.at).toLocaleTimeString(),
  },
  {
    name: 'Working set',
    mono: true,
    num: true,
    cell: (sample) => `${formatNumber(sample.working_set_mb)} MB`,
  },
  {
    name: 'Managed',
    mono: true,
    num: true,
    cell: (sample) => `${formatNumber(sample.managed_mb)} MB`,
  },
  { name: 'Heap', mono: true, num: true, cell: (sample) => `${formatNumber(sample.heap_mb)} MB` },
  {
    name: 'Processors',
    mono: true,
    num: true,
    cell: (sample) => (sample.cpu_percent === null ? 'first' : `${sample.cpu_percent}%`),
  },
  { name: 'Threads', mono: true, num: true, cell: (sample) => sample.threads },
];

/**
 * The container as it reports itself: a line of words, a memory gauge, the
 * catalogues in memory, a chart and a table. The heading says when the window
 * on the tab is wider than the hour this process remembers.
 */
export function MachinesContainerSection({
  container,
  window: window_,
}: {
  container: Machines['container'];
  window: MachineWindow;
}) {
  const samples = container.samples;
  const latest = samples.length > 0 ? samples[samples.length - 1] : null;
  const peak = samples.reduce((most, sample) => Math.max(most, sample.working_set_mb), 0);

  return (
    <>
      <h3 className={styles.cardTitle}>
        The container{window_ === '1h' ? '' : ', as this process remembers the last hour'}
      </h3>
      {latest === null ? (
        <p className={styles.muted} data-testid="machines-no-samples">
          No sample yet. One is taken every {container.every_seconds} seconds.
        </p>
      ) : (
        <>
          <p data-testid="machines-container-line">
            <strong>
              {latest.working_set_mb} MB of {container.memory_limit_mb} MB
            </strong>{' '}
            in use, {latest.managed_mb} MB of it managed objects,{' '}
            {latest.cpu_percent === null
              ? 'processor share not read yet'
              : `${latest.cpu_percent}% of ${container.processors} processor${container.processors === 1 ? '' : 's'}`}
            , {latest.threads} threads, {latest.gen0_collections} quick collections and{' '}
            {latest.gen2_collections} full ones since this container started. Peak working set in
            the window: {peak} MB.
          </p>
          <BarGauge
            testId="machines-memory-gauge"
            name="Memory"
            ceiling={`${container.memory_limit_mb.toLocaleString()} MB`}
            value={latest.working_set_mb}
            max={container.memory_limit_mb}
            reading={`${Math.round((latest.working_set_mb / Math.max(1, container.memory_limit_mb)) * 100)} % · ${latest.working_set_mb.toLocaleString()} MB`}
          />
          {container.catalogues && container.catalogues.length > 0 && (
            // Most of that memory is catalogues, a hundred thousand vehicles a
            // store, so the card says which ones the process is holding. The
            // store this site does not serve is loaded on demand and let go
            // when nobody has asked for it in a while (ADR: One plan, two sites).
            <p className={styles.muted} data-testid="machines-catalogues">
              Catalogues in memory:{' '}
              {container.catalogues
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
                name: `Memory, share of ${formatNumber(container.memory_limit_mb)} MB`,
                points: samples.map((sample) => ({
                  at: sample.at,
                  value:
                    container.memory_limit_mb > 0
                      ? Math.round((sample.working_set_mb / container.memory_limit_mb) * 1000) / 10
                      : null,
                })),
              },
              {
                key: 'cpu',
                name: `Processor, share of ${container.processors}`,
                points: samples.map((sample) => ({ at: sample.at, value: sample.cpu_percent })),
              },
            ]}
          />
          <DataTable
            label="The container, sampled"
            testId="machines-container-table"
            rows={[...samples].reverse().slice(0, 20)}
            rowKey={(sample) => sample.at}
            columns={CONTAINER_COLUMNS}
          />
        </>
      )}
    </>
  );
}
