/**
 * The relational store's part of the machines card: the newest reading of its
 * own resource view in words, a chart of the last hour and the newest twenty
 * rows, every figure a share of what the tier allows. Its own file because
 * this store reports in shares of a tier, unlike the container or the
 * document store.
 */
import styles from '../shared/card.module.css';
import type { Machines, ResourceStatRow } from '../shared/types';
import { MachineChart } from '../charts';
import { type Column, DataTable } from '../DataTable';

/** The relational store's own reading: each share of what its tier allows. */
const RELATIONAL_COLUMNS: Column<ResourceStatRow>[] = [
  { name: 'At', mono: true, short: true, cell: (row) => new Date(row.at).toLocaleTimeString() },
  { name: 'Processor', mono: true, num: true, cell: (row) => `${row.cpu_percent}%` },
  { name: 'Memory', mono: true, num: true, cell: (row) => `${row.memory_percent}%` },
  { name: 'Data', mono: true, num: true, cell: (row) => `${row.data_io_percent}%` },
  { name: 'Log', mono: true, num: true, cell: (row) => `${row.log_write_percent}%` },
  { name: 'Workers', mono: true, num: true, cell: (row) => `${row.worker_percent}%` },
];

/**
 * The relational store as it reports itself, or the note that says why it
 * cannot. The rows arrive newest first, so the chart reverses them to run
 * left to right in time.
 */
export function MachinesRelationalSection({ relational }: { relational: Machines['relational'] }) {
  const newest = relational.rows.length > 0 ? relational.rows[0] : null;
  const worst = relational.rows.reduce(
    (most, row) => Math.max(most, row.cpu_percent, row.data_io_percent, row.log_write_percent),
    0
  );

  return (
    <>
      <h3 className={styles.cardTitle}>{relational.store}</h3>
      {!relational.available ? (
        <p className={styles.muted} data-testid="machines-relational-note">
          {relational.note}
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
                points: [...relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.memory_percent })),
              },
              {
                key: 'cpu',
                name: 'Processor, share of the tier',
                points: [...relational.rows]
                  .reverse()
                  .map((row) => ({ at: row.at, value: row.cpu_percent })),
              },
            ]}
          />
          <DataTable
            label="The relational store's own reading"
            testId="machines-relational-table"
            rows={relational.rows.slice(0, 20)}
            rowKey={(row) => row.at}
            columns={RELATIONAL_COLUMNS}
          />
        </>
      )}
    </>
  );
}
