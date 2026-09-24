/**
 * Same performance, proven (ADR: Same performance, proven): the run, and the card
 * that shows it.
 */
import { useState } from 'react';
import { pairedBars } from '../../lib/machineChart';
import styles from '../AdminPanel.module.css';
import type { Fetched, Proof } from './types';
import { useRead, About } from './common';

function ProofBody({
  proof,
  signedIn,
  onRun,
  onOpenAccount,
}: {
  proof: Fetched<Proof>;
  signedIn: boolean;
  onRun: () => void;
  onOpenAccount: () => void;
}) {
  const running = proof !== null && proof !== 'failed' && proof.status === 'running';
  const result = proof !== null && proof !== 'failed' ? proof.result : null;
  return (
    <article className={`${styles.wide} op-glass`} data-testid="proof-card">
      <h2 className={styles.cardTitle}>Same performance, proven</h2>
      <About>
        The same requests a visitor makes, sent by this container to itself on both stores in paired
        rounds that alternate which store goes first: identical process, identical request, only the
        store differs. Each row is one path; the difference is the median of the paired differences,
        and the last column but one takes one round trip per store operation off each side, so the
        difference the stores make can be told from the difference their distance makes. The proof
        bids with two accounts of its own, one per store, made once and kept, and a run takes about
        half a minute. Reading the result is open to anyone; starting a run is a write, so it takes
        a signed-in visitor, like every other write here.
      </About>
      <p>
        {/* Starting a run writes sixteen bids, so the button follows the one
            rule every write on this site follows (ADR: The one write a
            stranger can make, addendum): signed out, it says what it needs
            and takes the visitor there. It sat disabled at first, a button
            that read like a call to action and did nothing, which on a phone
            reads as broken (Steve, 13 September; ADR: Same performance,
            proven, addendum). */}
        <button
          type="button"
          className={styles.back}
          onClick={signedIn ? onRun : onOpenAccount}
          disabled={running}
          data-testid="proof-run"
        >
          {!signedIn
            ? 'Sign in to run the proof'
            : running
              ? 'Running…'
              : result === null
                ? 'Run the proof'
                : 'Run it again'}
        </button>
      </p>
      {proof === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : proof === 'failed' ? (
        <p className={styles.muted} data-testid="card-failed">
          Could not read the proof on the last try; the next try is in 30 seconds.
        </p>
      ) : result === null ? (
        <p className={styles.muted} data-testid="proof-note">
          {running
            ? 'Running. The result lands here within a minute.'
            : 'Not run yet on this container. The button runs it; the result stays until the next deploy.'}
        </p>
      ) : result.status === 'failed' ? (
        <p className={styles.muted} data-testid="proof-note">
          {result.reason}
        </p>
      ) : (
        <>
          <p data-testid="proof-sentence">{result.sentence}</p>
          <p className={styles.muted}>
            {result.rounds} paired rounds, finished{' '}
            {result.finished_at ? new Date(result.finished_at).toLocaleTimeString() : ''}. One round
            trip to the store:{' '}
            {result.stores.map((s) => `${s.hop_ms ?? '?'} ms to ${s.name}`).join(', ')}.
          </p>
          {/* The paired medians as bars, so the eye reads what the table says:
              most pairs are the same length, and the ones that are not differ
              by a round trip (ADR: The Admin tab, as a product). */}
          <ul
            className={styles.pairList}
            aria-label="The paired medians, each bar a share of the longest on the card"
            data-testid="proof-bars"
          >
            {pairedBars(result.rows).map((pair) => (
              <li key={pair.label} className={styles.pairRow}>
                <span className={styles.pairLabel}>{pair.label}</span>
                <span className={styles.pairBars}>
                  {pair.bars.map((bar, index) => (
                    <span key={bar.store} className={styles.pairBarRow}>
                      <svg
                        className={`${styles.pairTrack} ${index === 0 ? styles.sqlLine : styles.cosmosLine}`}
                        viewBox="0 0 100 8"
                        preserveAspectRatio="none"
                        aria-hidden="true"
                      >
                        <rect className={styles.pairBar} width={bar.share} height="8" rx="2" />
                      </svg>
                      <span className={styles.mono}>
                        {bar.ms} ms, {bar.store}
                      </span>
                    </span>
                  ))}
                </span>
              </li>
            ))}
          </ul>
          <div className={styles.tableWrap} role="region" aria-label="The proof" tabIndex={0}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Path</th>
                  {result.stores.map((s) => (
                    <th scope="col" key={s.key}>
                      {s.name}
                    </th>
                  ))}
                  <th scope="col">Difference</th>
                  <th scope="col">Without the round trips</th>
                  <th scope="col">Verdict</th>
                </tr>
              </thead>
              <tbody>
                {result.rows.map((row) => (
                  <tr key={row.path}>
                    <td>{row.label}</td>
                    {row.cells.map((cell) => (
                      <td className={styles.mono} key={cell.store}>
                        {cell.samples === 0
                          ? 'not measured'
                          : `p50 ${cell.p50_ms} ms, p95 ${cell.p95_ms} ms (${cell.samples})` +
                            (cell.request_units_per_request === null
                              ? ''
                              : ` · ${cell.request_units_per_request} RU`) +
                            (cell.operations_per_request > 0
                              ? `, ${cell.operations_per_request} ops`
                              : '')}
                      </td>
                    ))}
                    <td className={styles.mono}>
                      {row.median_difference_ms === null ? '' : signed(row.median_difference_ms)}
                    </td>
                    <td className={styles.mono}>
                      {row.difference_without_hops_ms === null
                        ? ''
                        : signed(row.difference_without_hops_ms)}
                    </td>
                    <td>{row.verdict}</td>
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

/** A difference with its sign, so a column of them reads as a column. */
function signed(ms: number): string {
  return ms > 0 ? `+${ms} ms` : `${ms} ms`;
}

export default function ProofCard({
  tick,
  signedIn,
  onOpenAccount,
}: {
  tick: number;
  signedIn: boolean;
  onOpenAccount: () => void;
}) {
  const read = useRead<Proof>('/api/admin/proof', tick);
  // A run in hand outranks the clock's read until the run lands.
  const [running, setRunning] = useState<Fetched<Proof>>(null);
  const proof = running ?? read;
  // #region run-proof
  // Start a run, then read the card every three seconds until it is no longer
  // running, because the thirty-second refresh above would leave the button
  // saying "Running" long after the result had landed.
  const runProof = async () => {
    const started = await fetch('/api/admin/proof', { method: 'POST' }).catch(() => null);
    if (started === null || (!started.ok && started.status !== 409)) {
      setRunning('failed');
      return;
    }
    setRunning({
      status: 'running',
      result: proof !== null && proof !== 'failed' ? proof.result : null,
    });
    const poll = window.setInterval(() => {
      void fetch('/api/admin/proof')
        .then((r) =>
          r.ok ? (r.json() as Promise<Proof>) : Promise.reject(new Error(String(r.status)))
        )
        .then((latest) => {
          if (latest.status !== 'running') {
            window.clearInterval(poll);
            setRunning(latest);
          }
        })
        .catch(() => {
          window.clearInterval(poll);
          setRunning('failed');
        });
    }, 3000);
  };
  // #endregion run-proof
  return (
    <ProofBody
      proof={proof}
      signedIn={signedIn}
      onRun={() => void runProof()}
      onOpenAccount={onOpenAccount}
    />
  );
}
