/**
 * Does:      Draws the short path's first tile as the whole-page picture draws it: the quality number's readings as one
 *            plain line stretched to the tile, the soft area up to the ratchet's version, the gold dashed mark at it,
 *            the three captions under it, and at its foot the quality numbers this version held.
 * Does not:  Read the readings (useReviewPath.ts does), scale them (reviewPath.ts's sparkGeometry does) or type a number:
 *            before they arrive the tile keeps its height with the line not yet drawn.
 * Used by:   ReviewPath.tsx.
 */
import RELEASES from 'virtual:release-facts';
import { PATH, SPARK, sparkGeometry } from '../../../lib/reviewPath';
import type { HeldQuality } from '../../../lib/reviewVerdict';
import { TickDisc } from '../TickDisc';
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
  const spark = sparkGeometry(readings, RELEASES.quality.marker);
  return (
    <div className={styles.shot} data-testid="review-path-trend">
      <p className={styles.cap}>{label}</p>
      <span className={styles.live}>Live</span>
      <svg
        className={styles.spark}
        viewBox={`0 0 ${SPARK.width} ${SPARK.height}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={readings === null ? label : `${label}: ${PATH.captions.join(', ')}`}
        data-testid="review-path-spark"
      >
        <path
          className={styles.sparkBase}
          d={`M0 ${SPARK.floor} H${SPARK.width}`}
          vectorEffect="non-scaling-stroke"
        />
        {spark && (
          <>
            <path className={styles.sparkArea} d={spark.area} />
            <path
              className={styles.sparkBefore}
              d={spark.before}
              vectorEffect="non-scaling-stroke"
            />
            <path className={styles.sparkAfter} d={spark.after} vectorEffect="non-scaling-stroke" />
            <path
              className={styles.sparkMark}
              d={`M${spark.markX} 0 V${SPARK.floor}`}
              vectorEffect="non-scaling-stroke"
            />
          </>
        )}
      </svg>
      <p className={styles.axis}>
        {PATH.captions.map((caption) => (
          <span key={caption}>{caption}</span>
        ))}
      </p>
      {quality !== null && (
        <p className={styles.foot}>
          <TickDisc />
          <span>
            {quality.held} of {quality.of} quality numbers held on this version
          </span>
        </p>
      )}
    </div>
  );
}
