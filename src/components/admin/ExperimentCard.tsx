/**
 * The partition key, live (ADR: The partition key).
 */
import styles from '../AdminPanel.module.css';
import type { Experiment } from './types';
import { useRead, failed, About } from './common';

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
            <div
              className={styles.tableWrap}
              role="region"
              aria-label="Queries against the partitioned catalogue"
              tabIndex={0}
            >
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Query</th>
                    <th scope="col" className={styles.num}>
                      Partitions
                    </th>
                    <th scope="col" className={styles.num}>
                      Charge
                    </th>
                    <th scope="col" className={styles.num}>
                      Took
                    </th>
                    <th scope="col" className={styles.num}>
                      Documents
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {experiment.rows.map((row) => (
                    <tr key={row.query}>
                      <td>{row.query}</td>
                      <td className={`${styles.mono} ${styles.num}`}>{row.partitions}</td>
                      <td className={`${styles.mono} ${styles.num}`}>{row.request_charge} RU</td>
                      <td className={`${styles.mono} ${styles.num}`}>{row.duration_ms} ms</td>
                      <td className={`${styles.mono} ${styles.num}`}>{row.documents}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </article>
      {/* #endregion experiment-card */}
    </>
  );
}
