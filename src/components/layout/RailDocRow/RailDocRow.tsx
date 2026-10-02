/**
 * A sidebar row that opens a document in the dialog: its kind's icon, its
 * record number when it has one, and its menu label. Its own file because it
 * is the row the sidebar draws most, and it takes the document's words as
 * plain values so it does not need the library to draw them.
 */
import { RowIcon, type RowKind } from '../../shared/SheetIcons';
import styles from '../SideNav/SideNav.module.css';

/**
 * One document's row. It reads as the current page while its document is
 * open; a sub row is indented under the row before it. On the icons-only rail
 * the words are hidden and the label becomes the tooltip.
 */
export function RailDocRow({
  label,
  number,
  kind,
  sub,
  current,
  iconsOnly,
  onOpen,
}: {
  label: string;
  number?: string;
  kind: RowKind;
  sub?: boolean;
  current: boolean;
  iconsOnly: boolean;
  onOpen: () => void;
}) {
  return (
    <button
      type="button"
      className={sub ? `${styles.row} ${styles.subRow}` : styles.row}
      onClick={onOpen}
      aria-current={current ? 'page' : undefined}
      title={iconsOnly ? label : undefined}
    >
      <RowIcon kind={kind} className={styles.icon} />
      <span className={iconsOnly ? styles.srOnly : styles.label}>
        {number ? (
          <>
            <span className={styles.recordNumber}>{number}</span>{' '}
          </>
        ) : null}
        {label}
      </span>
    </button>
  );
}
