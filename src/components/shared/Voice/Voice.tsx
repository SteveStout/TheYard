/**
 * Does:      Draws one named voice beside a claim, as the whole-page pictures draw it: the person's one line from
 *            /api/recommendations in quotes, their name and title as the answer gives them, and "Read it in full"; the
 *            whole block is one link to their card on the recommendations page and reports the press. Drawn as its own
 *            glass tile, or plain inside a panel that already has one.
 * Does not:  Read the API (the page already holds the answer and hands it over) or pick the name (voices.ts does).
 *            A name the answer does not hold draws nothing, so its place closes up.
 * Used by:   ReviewPath.tsx, ReviewPage.tsx, ReviewClose.tsx.
 */
import type { Recommendation } from '../../../lib/recommendations';
import { cardAddress } from '../../../lib/recommendationsPage';
import { READ_IN_FULL, voiceOf } from '../../../lib/voices';
import styles from './Voice.module.css';

export function Voice({
  name,
  recommendations,
  onFollow,
  plain = false,
  className = '',
  column,
}: {
  name: string;
  /** The /api/recommendations answer the page holds, or null before it arrives. */
  recommendations: readonly Recommendation[] | null;
  /** Reports the press, as the page's other ways to the recommendations do. */
  onFollow: () => void;
  /** Inside a panel of its own: no glass, no padding. */
  plain?: boolean;
  /** The placing a grid gives it, from the page that lays it out. */
  className?: string;
  /** The grid column it stands under, when the page places it by column. */
  column?: number;
}) {
  const entry = voiceOf(recommendations, name);
  if (entry === null) return null;
  return (
    <a
      className={`${styles.voice} ${plain ? styles.plain : 'op-glass op-tile'} ${className}`}
      href={cardAddress(entry.name)}
      onClick={onFollow}
      data-action="review.recommendations"
      data-testid="voice"
      data-name={entry.name}
      data-column={column}
    >
      <span className={styles.say} data-testid="voice-pull">
        {`"${entry.pull}"`}
      </span>
      <span className={styles.sig}>
        <span>
          <strong>{entry.name}</strong>
          <br />
          {entry.title}
        </span>
        <span className={styles.go}>{READ_IN_FULL}</span>
      </span>
    </a>
  );
}
