import { RESERVE_STATE_LABELS, type ReserveState } from '../lib/auction';
import styles from './ReserveBadge.module.css';

interface ReserveBadgeProps {
  state: ReserveState;
}

/** Shows only the reserve state — the reserve amount is never displayed. */
export function ReserveBadge({ state }: ReserveBadgeProps) {
  const tone =
    state === 'met' ? styles.met : state === 'no-reserve' ? styles.noReserve : styles.notMet;
  return <span className={`${styles.badge} ${tone}`}>{RESERVE_STATE_LABELS[state]}</span>;
}
