/**
 * A number's normal range, said in one quiet line under it: "Normal: memory
 * of its limit, amber at 80%, red at 95%". The lines come from src/lib/ranges.ts,
 * the one table the tiles and the Overview read too, so a card and its tile
 * cannot disagree about where amber starts.
 */
import { RANGES, rangeWords, type RangeKey } from '../../../lib/ranges';
import styles from './card.module.css';

/** The range's name as it follows "Normal:", lower case unless it opens with a proper noun. */
function afterColon(name: string): string {
  return /^(Azure|SQL)/.test(name) ? name : name.charAt(0).toLowerCase() + name.slice(1);
}

export function Range({ of }: { of: RangeKey }) {
  return (
    <p className={styles.muted} data-testid={`range-${of}`}>
      Normal: {afterColon(RANGES[of].name)}, {rangeWords(of)}.
    </p>
  );
}
