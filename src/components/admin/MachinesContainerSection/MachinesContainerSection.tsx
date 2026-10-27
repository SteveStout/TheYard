/**
 * The container's part of the machines card, in the three places the shell's shape puts it: its line of words
 * and the catalogues it holds in the readout, its memory gauge against the runtime's own limit in the lead,
 * and a chart of the sampled window with the newest twenty samples at full width below. Its own file because
 * the container reports differently from either store and reads on its own.
 */
import { More } from '../shared/More';
import styles from '../shared/card.module.css';
import areas from '../shared/cardAreas.module.css';
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

/** The newest sample and the peak working set, which the three parts read. */
function latestOf(container: Machines['container']) {
  const samples = container.samples;
  return {
    latest: samples.length > 0 ? samples[samples.length - 1] : null,
    peak: samples.reduce((most, sample) => Math.max(most, sample.working_set_mb), 0),
  };
}

/** The container in one line of words, or the sentence that says no sample has been taken, and the catalogues it holds. */
export function ContainerLine({
  container,
  window: window_,
}: {
  container: Machines['container'];
  window: MachineWindow;
}) {
  const { latest, peak } = latestOf(container);
  return (
    <div className={areas.asideLine}>
      <h4 className={areas.asideName}>
        The container{window_ === '1h' ? '' : ', as this process remembers the last hour'}
      </h4>
      {latest === null ? (
        <p className={styles.muted} data-testid="machines-no-samples">
          No sample yet. One is taken every {container.every_seconds} seconds.
        </p>
      ) : (
        <p data-testid="machines-container-line">
          <strong>
            {formatNumber(latest.working_set_mb)} MB of {formatNumber(container.memory_limit_mb)} MB
          </strong>{' '}
          in use, {formatNumber(latest.managed_mb)} MB of it managed objects,{' '}
          {latest.cpu_percent === null
            ? 'processor share not read yet'
            : `${latest.cpu_percent}% of ${container.processors} processor${container.processors === 1 ? '' : 's'}`}
          , {latest.threads} threads, {latest.gen0_collections} quick collections and{' '}
          {latest.gen2_collections} full ones since this container started. Peak working set in the
          window: {formatNumber(peak)} MB.
        </p>
      )}
      {container.catalogues && container.catalogues.length > 0 && (
        // Most of that memory is catalogues, a hundred thousand vehicles a
        // store, so the card says which ones the process is holding. The
        // store this site does not serve is loaded by the keep-warm loop and
        // kept, so a switch to it never meets a cold read (ADR: Kept awake).
        <p className={styles.muted} data-testid="machines-catalogues">
          Catalogues in memory:{' '}
          {container.catalogues
            .map(
              (catalogue) =>
                `${catalogue.store}, ${catalogue.serves ? 'which this site serves' : 'kept warm for a switch'}, ${catalogue.loaded ? 'held' : 'not held'}`
            )
            .join('; ')}
          .
        </p>
      )}
    </div>
  );
}

/** The memory gauge: the working set against the runtime's own limit. Nothing while no sample has been taken. */
export function ContainerGauge({ container }: { container: Machines['container'] }) {
  const { latest } = latestOf(container);
  if (latest === null) return null;
  return (
    <BarGauge
      testId="machines-memory-gauge"
      name="Memory"
      ceiling={`${formatNumber(container.memory_limit_mb)} MB`}
      value={latest.working_set_mb}
      max={container.memory_limit_mb}
      reading={`${Math.round((latest.working_set_mb / Math.max(1, container.memory_limit_mb)) * 100)}% · ${formatNumber(latest.working_set_mb)} MB`}
    />
  );
}

/** The container's chart of the sampled window and its newest twenty samples, at the card's full width. */
export function ContainerDetail({ container }: { container: Machines['container'] }) {
  const samples = container.samples;
  if (samples.length === 0) return null;
  return (
    <div className={areas.section}>
      <h3 className={styles.cardTitle}>The container, sampled</h3>
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
      <More label="the newest 20 samples" testId="containersection-more">
        <DataTable
          label="The container, sampled"
          testId="machines-container-table"
          rows={[...samples].reverse().slice(0, 20)}
          rowKey={(sample) => sample.at}
          columns={CONTAINER_COLUMNS}
        />
      </More>
    </div>
  );
}
