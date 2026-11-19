/**
 * Does:      Draws one proof tile on the review route's first screen (ADR: The review route gives a reason before it
 *            asks): the figure, what it counts and where it comes from, on the stat tile's glass, with a LIVE mark
 *            beside a figure read from this build. An empty figure keeps the tile's height until it arrives.
 * Does not:  Pick or read a figure (reviewVerdict.ts and the first screen do).
 * Used by:   ReviewVerdict.tsx.
 */
import type { VerdictTile } from '../../../lib/reviewVerdict';
import styles from './ProofTile.module.css';

export function ProofTile({ tile }: { tile: VerdictTile }) {
  return (
    <li className={`${styles.tile} op-glass op-tile`} data-testid={`review-proof-${tile.key}`}>
      <p className={styles.figure}>
        <span data-testid={`review-proof-${tile.key}-figure`}>{tile.figure}</span>
        {tile.live && (
          <span className={styles.live}>
            <span className={styles.dot} aria-hidden="true" />
            Live
          </span>
        )}
      </p>
      <p className={styles.label}>{tile.label}</p>
      <p className={styles.source}>{tile.source}</p>
    </li>
  );
}
