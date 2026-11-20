/**
 * Does:      Draws one proof tile on the review route's first screen (ADR: The review route gives a reason before it
 *            asks): the figure, what it counts and where it comes from, on the stat tile's glass, with a LIVE mark
 *            beside a figure read from this build, and last its door, "See how", to the case card under "01 The work"
 *            that tells the number's story (ADR: The sales pages set the site's look). The door reports work and
 *            scrolls there; on a phone the tile is its figure, its label and its door, the case card carrying the
 *            sentence. An empty figure keeps the tile's height until it arrives.
 * Does not:  Pick or read a figure (reviewVerdict.ts and the first screen do).
 * Used by:   ReviewVerdict.tsx.
 */
import { TILE_DOOR, type VerdictTile } from '../../../lib/reviewVerdict';
import { scrollToSection } from '../../../lib/sectionScroll';
import styles from './ProofTile.module.css';

export function ProofTile({ tile, onDoor }: { tile: VerdictTile; onDoor: () => void }) {
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
      <a
        className={`op-tile-door ${styles.door}`}
        href={`#${tile.door}`}
        onClick={(event) => {
          onDoor();
          scrollToSection(event, tile.door);
        }}
        data-action="review.work"
        data-mock="tile-door"
      >
        {TILE_DOOR} <span aria-hidden="true">↓</span>
      </a>
    </li>
  );
}
