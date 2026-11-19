/**
 * Does:      Draws the short path's third tile: a live lot right now, its name and the bid it stands at with the room
 *            bidding, and the vehicles in the store; when no auction is live it says so.
 * Does not:  Read the lot (useReviewPath.ts does, from one answer kept fifteen seconds) or pick it.
 * Used by:   ReviewPath.tsx.
 */
import { bidWords, PATH, storeTotal, type PathLot } from '../../../lib/reviewPath';
import styles from '../ReviewPath/ReviewPath.module.css';

export function LotTile({ label, lot }: { label: string; lot: PathLot | null }) {
  return (
    <div className={styles.reading} data-testid="review-path-lot">
      <p className={styles.tileLabel}>
        <span>{label}</span>
        <span className={styles.live}>Live</span>
      </p>
      {lot !== null && !lot.live ? (
        <p className={styles.lotName}>{PATH.noLot}</p>
      ) : (
        <>
          <p className={styles.lotName}>{lot?.name ?? ''}</p>
          <p className={styles.bidLabel}>Current bid</p>
          <p className={styles.bid} data-testid="review-path-bid">
            {bidWords(lot)}
          </p>
          <p className={styles.tileLine}>{PATH.bidding}</p>
        </>
      )}
      <p className={styles.tileLine}>
        <span data-testid="review-path-total">{storeTotal(lot)}</span> vehicles in the store
      </p>
    </div>
  );
}
