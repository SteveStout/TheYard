/**
 * Does:      Draws one proof tile on the review route's first screen (ADR: The review route gives a reason before it
 *            asks): the figure, what it counts and where it comes from, on the stat tile's glass, with a LIVE mark
 *            beside a figure read from this build, and last its door, "See how", to the case card under "01 The work"
 *            that tells the number's story (ADR: The sales pages set the site's look). The door reports work and
 *            scrolls there; on a phone the tile is its figure, its label and its door, the case card carrying the
 *            sentence. An empty figure keeps the tile's height until it arrives. On a page other than the review route
 *            the door leaves for that card on the review route, so its arrow points across.
 * Does not:  Pick or read a figure (reviewVerdict.ts and the first screen do).
 * Used by:   ReviewVerdict.tsx, AboutPage.tsx.
 */
import { REVIEW_ADDRESS } from '../../../lib/reviewStops';
import { TILE_DOOR, type VerdictTile } from '../../../lib/reviewVerdict';
import { scrollToSection } from '../../../lib/sectionScroll';
import styles from './ProofTile.module.css';

export function ProofTile({
  tile,
  onDoor,
  leaves = false,
  action = 'review.work',
}: {
  tile: VerdictTile;
  onDoor: () => void;
  /** The door leaves this page for the case card on the review route, rather than scrolling down to it. */
  leaves?: boolean;
  /** What a press on the door reports. */
  action?: string;
}) {
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
        href={leaves ? `${REVIEW_ADDRESS}#${tile.door}` : `#${tile.door}`}
        onClick={(event) => {
          onDoor();
          if (!leaves) scrollToSection(event, tile.door);
        }}
        data-action={action}
        data-mock="tile-door"
      >
        {TILE_DOOR} <span aria-hidden="true">{leaves ? '→' : '↓'}</span>
      </a>
    </li>
  );
}
