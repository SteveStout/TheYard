/**
 * A number's normal range, said in one quiet line under it: "Normal: memory
 * of its limit, amber at 80%, red at 95%". The lines come from src/lib/ranges.ts,
 * the one table the tiles and the Overview read too, so a card and its tile
 * cannot disagree about where amber starts.
 */
import { RANGES, rangeWords, type RangeKey } from '../../../lib/ranges';
import styles from './card.module.css';

export function Range({ of }: { of: RangeKey }) {
  return (
    <p className={styles.muted} data-testid={`range-${of}`}>
      Normal: {RANGES[of].name.toLowerCase()}, {rangeWords(of)}.
    </p>
  );
}
