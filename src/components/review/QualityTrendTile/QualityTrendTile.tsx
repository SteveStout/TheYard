/**
 * Does:      Draws the short path's first tile: the chart at the top of the Quality page at tile size, with the same
 *            gold mark, its three captions, and the quality numbers this version held when the build has a reading.
 * Does not:  Read the readings (useReviewPath.ts does) or type a number: before they arrive the tile keeps its height.
 * Used by:   ReviewPath.tsx.
 */
import RELEASES from 'virtual:release-facts';
import { PATH, pathTrend } from '../../../lib/reviewPath';
import type { HeldQuality } from '../../../lib/reviewVerdict';
import { QualityTrend } from '../../shared/QualityTrend';
import styles from '../ReviewPath/ReviewPath.module.css';

export function QualityTrendTile({
  label,
  readings,
  quality,
}: {
  label: string;
  readings: string[] | null;
  quality: HeldQuality;
}) {
  const data = pathTrend(readings, RELEASES.quality.marker);
  return (
    <div className={styles.reading} data-testid="review-path-trend">
      <p className={styles.tileLabel}>
        <span>{label}</span>
        <span className={styles.live}>Live</span>
      </p>
      <div className={styles.chart}>{data && <QualityTrend data={data} />}</div>
      <p className={styles.captions}>
        {PATH.captions.map((caption) => (
          <span key={caption}>{caption}</span>
        ))}
      </p>
      {quality !== null && (
        <p className={styles.tileLine}>
          {quality.held} of {quality.of} quality numbers held on this version
        </p>
      )}
    </div>
  );
}
