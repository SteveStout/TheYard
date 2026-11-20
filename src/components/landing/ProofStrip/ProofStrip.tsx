/**
 * Does:      Draws the home page's three figures from this build's own gate (the tests passed, the decision records, the
 *            stores up), in the build row under Tech stack: blank places of the same size until the gate's counts
 *            arrive, so nothing under them moves, and a ring on the two that are a share of a whole.
 * Does not:  Read anything (the page hands over the tests summary and the health answer's stores) or lay out the row.
 * Used by:   Landing.tsx.
 */
import {
  proofFigures,
  ringReading,
  type ProofFigure,
  type TestSummary,
} from '../../../lib/landingProof';
import { MENUS } from '../../../library/sections';
import { CountUp } from '../../shared/CountUp';
import { Ring } from '../../shared/Ring';
import styles from '../Landing/Landing.module.css';

/** The two figures that are a share of a whole, and so carry a ring (the operator's look). */
const RINGED = ['tests', 'stores'];

/** The strip's three places before the gate's counts arrive: the same boxes, blank, so nothing moves when they fill. */
const PROOF_PLACEHOLDER: ProofFigure[] = ['tests', 'records', 'stores'].map((key) => ({
  key,
  figure: '',
  label: '',
  detail: '',
}));

export function ProofStrip({
  summary,
  stores,
}: {
  summary: TestSummary | null;
  /** How many stores answered, of how many run, from the health answer; null before it. */
  stores: { up: number; of: number } | null;
}) {
  // The record count is the sidebar's own list, so the strip cannot claim records the site does not open.
  const proof = proofFigures(summary, MENUS.records.items.length);
  // The strip stands at its full height from the first paint, three blank figures until the gate's counts arrive:
  // drawn only once they had, it pushed every group below it down 124 px.
  return (
    <ul
      className={styles.proof}
      aria-label="What this build's gate measured"
      aria-busy={proof === null}
      data-testid="landing-proof"
      data-state={proof === null ? 'loading' : 'ready'}
    >
      {(proof ?? PROOF_PLACEHOLDER).map((figure) => {
        // The stores are read live from the health answer: how many answered, of how many run.
        const ring =
          figure.key === 'stores' && stores !== null
            ? {
                value: stores.up,
                max: stores.of,
                words: `${stores.up} of ${stores.of} stores up`,
              }
            : figure.ring;
        return (
          <li
            key={figure.key}
            className={`${styles.proofItem} op-glass op-tile`}
            data-testid={`landing-proof-${figure.key}`}
          >
            <span className={styles.proofFigure}>
              <CountUp text={figure.figure || ' '} />
            </span>
            <span className={styles.proofLabel}>{figure.label || ' '}</span>
            <span className={styles.proofDetail}>{figure.detail || ' '}</span>
            {RINGED.includes(figure.key) && (
              // Drawn from the first paint, an empty track until its reading
              // arrives, so the figure's row is its full height from the start.
              <span className={styles.proofRing}>
                <Ring
                  value={ring?.value ?? 0}
                  max={ring?.max ?? 1}
                  size="tile"
                  inside={ringReading(figure.key, ring)}
                  label={ring?.words ?? null}
                  testId={`landing-proof-ring-${figure.key}`}
                />
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}
