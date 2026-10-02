// The "This hour" panel some Admin cards show: two rings for response time and the hour's
// counts below them. It draws a summary already worked out in src/lib/bench.ts from the same
// data as the tiles, so the panel and the tiles always agree. It is its own file because the
// workbench places it either beside the open card or above it, and the panel is the same in both.
import { Readout } from '../../shared/Readout';
import { Ring } from '../../shared/Ring';
import type { HourGlance } from '../../../lib/bench';
import styles from '../AdminPanel/AdminPanel.module.css';

/**
 * The "This hour" panel: two rings for response time (p95 outside, p50
 * inside, both against the same scale), the number in words in the middle,
 * and the hour's counts below.
 * It uses the same data as the tiles, so the two always agree.
 */
export function AdminHourAtAGlance({
  glance,
  beside,
}: {
  /** The hour's summary: ring values, the words inside and the rows of counts. */
  glance: HourGlance;
  /** True when the panel sits in a column beside the card, which draws the rings larger. */
  beside: boolean;
}) {
  return (
    <section
      className={`${beside ? styles.hourBeside : styles.hourAbove} op-glass`}
      aria-labelledby="bench-hour-title"
      data-testid="bench-hour"
    >
      <h2 className={styles.hourTitle} id="bench-hour-title">
        This hour
      </h2>
      <div className={styles.hourBody}>
        <Ring
          value={glance.ring?.p95 ?? 0}
          max={glance.ring?.max ?? 1}
          second={{ value: glance.ring?.p50 ?? 0, max: glance.ring?.max ?? 1 }}
          size={beside ? 'large' : 'page'}
          inside={glance.inside}
          label={glance.label}
          testId="bench-hour-ring"
          graduated
        />
        <Readout rows={glance.rows} testId="bench-hour-readout" />
      </div>
    </section>
  );
}
