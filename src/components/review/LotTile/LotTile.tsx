/**
 * Does:      Draws the short path's third tile as the whole-page picture draws it: a live lot right now, which the room
 *            has opened, its name, its bid with three dots and "the room is bidding" (or the opening ask when nobody
 *            has bid), and at its foot the time it closes in, counted down on the page's clock. "No auction is live
 *            right now" is drawn only when the catalogue holds no live auction, which the seeded catalogue never does.
 * Does not:  Read the lot (useReviewPath.ts does, from one answer kept fifteen seconds) or pick it (the API does).
 * Used by:   ReviewPath.tsx.
 */
import { closesIn, lotAmount, PATH, type PathLot } from '../../../lib/reviewPath';
import styles from '../ReviewPath/ReviewPath.module.css';

export function LotTile({
  label,
  lot,
  now,
}: {
  label: string;
  lot: PathLot | null;
  /** The page's clock, the one every countdown on the site reads. */
  now: number;
}) {
  const ends = lot?.live ? lot.ends_at_ms : null;
  const amount = lotAmount(lot);
  return (
    <div className={styles.shot} data-testid="review-path-lot">
      <p className={styles.cap}>{label}</p>
      <span className={styles.live}>Live</span>
      {lot !== null && !lot.live ? (
        <p className={styles.name}>{PATH.noLot}</p>
      ) : (
        <>
          <div className={styles.lot}>
            <div className={styles.car} aria-hidden="true">
              <svg viewBox="0 0 84 40" width="84" height="40" focusable="false">
                <path
                  d="M6 28 V22 l8 -3 8 -11 h28 l12 11 12 3 a4 4 0 0 1 4 4 v4 h-8 a8 8 0 0 0 -16 0 H30 a8 8 0 0 0 -16 0 z"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.2"
                  strokeLinejoin="round"
                />
                <circle
                  className={styles.wheel}
                  cx="22"
                  cy="29"
                  r="6"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.2"
                />
                <circle
                  className={styles.wheel}
                  cx="62"
                  cy="29"
                  r="6"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.2"
                />
              </svg>
            </div>
            <div>
              <p className={styles.name} data-testid="review-path-lot-name">
                {lot?.name ?? ''}
              </p>
              <p className={styles.bid}>
                <small>{amount.label}</small>
                <span data-testid="review-path-bid">{amount.amount}</span>
              </p>
              {amount.bidding && (
                <p className={styles.bids}>
                  <i aria-hidden="true" />
                  <i aria-hidden="true" />
                  <i aria-hidden="true" />
                  <span>{PATH.bidding}</span>
                </p>
              )}
            </div>
          </div>
          <p className={styles.foot}>
            {ends != null && (
              <span>
                {PATH.closes} <span data-testid="review-path-closes">{closesIn(ends, now)}</span>
              </span>
            )}
          </p>
        </>
      )}
    </div>
  );
}
