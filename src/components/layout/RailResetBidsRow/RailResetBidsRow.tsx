/**
 * The Reset bids row the sidebar shows under Admin while there are bids to
 * clear, with the count in its label. Its own file because it is not a place
 * in the site map but an action, and it appears only while it has work to do.
 */
import { RowIcon } from '../../shared/SheetIcons';
import styles from '../SideNav/SideNav.module.css';

/** The row that clears this visitor's bids; the count says how many there are. */
export function RailResetBidsRow({
  bidCount,
  iconsOnly,
  onReset,
}: {
  bidCount: number;
  iconsOnly: boolean;
  onReset: () => void;
}) {
  return (
    <button
      type="button"
      className={styles.row}
      onClick={onReset}
      title={iconsOnly ? `Reset bids (${bidCount})` : undefined}
    >
      <RowIcon kind="reset" className={styles.icon} />
      <span className={iconsOnly ? styles.srOnly : styles.label}>Reset bids ({bidCount})</span>
    </button>
  );
}
