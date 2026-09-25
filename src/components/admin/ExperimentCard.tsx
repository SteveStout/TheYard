/**
 * The partition key, live (ADR: The partition key).
 */
import styles from '../AdminPanel.module.css';
import type { Experiment, ExperimentRow } from './types';
import { useRead, failed, About } from './common';
import { type Column, DataTable } from './DataTable';

/** A query: what it asked, how many partitions it touched, and what that cost. */
const EXPERIMENT_COLUMNS: Column<ExperimentRow>[] = [
  { name: 'Query', cell: (row) => row.query },
  { name: 'Partitions', mono: true, num: true, cell: (row) => row.partitions },
  { name: 'Charge', mono: true, num: true, cell: (row) => `${row.request_charge} RU` },
  { name: 'Took', mono: true, num: true, cell: (row) => `${row.duration_ms} ms` },
  { name: 'Documents', mono: true, num: true, cell: (row) => row.documents },
];

export default function ExperimentCard({ tick }: { tick: number }) {
  const experiment = useRead<Experiment>('/api/admin/experiment', tick);
  return (
    <>
      {/* #region experiment-card */}
      <article className={`${styles.wide} op-glass`} data-testid="experiment-card">
        <h2 className={styles.cardTitle}>The partition key, live</h2>
        <About>
          Seven queries against a container of 100,000 vehicles partitioned on the make, run by this
          container with its own identity when this page asks, and cached for a minute. A query that
          names the make runs inside one logical partition; one that cannot fans out across every
          physical partition, and the request charge beside each is what that costs. The reasoning,
          the alternatives and the honest caveat about how many physical partitions there are at
          this size are in the partition key record.
        </About>
        {experiment === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : experiment === 'failed' ? (
          failed('the experiment')
        ) : !experiment.available ? (
          <p className={styles.muted} data-testid="experiment-note">
            Not available here: {experiment.reason}
          </p>
        ) : (
          <>
            <p className={styles.muted}>
              {experiment.container}: {experiment.documents?.toLocaleString()} documents on{' '}
              {experiment.physical_partitions} physical partition
              {experiment.physical_partitions === 1 ? '' : 's'}, measured at{' '}
              {experiment.ran_at ? new Date(experiment.ran_at).toLocaleTimeString() : ''}.
            </p>
            <DataTable
              label="Queries against the partitioned catalogue"
              rows={experiment.rows}
              rowKey={(row) => row.query}
              columns={EXPERIMENT_COLUMNS}
            />
          </>
        )}
      </article>
      {/* #endregion experiment-card */}
    </>
  );
}
