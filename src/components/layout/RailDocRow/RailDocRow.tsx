/**
 * A sidebar row that opens a document in the dialog: its kind's icon, its
 * record number when it has one, and its menu label. Its own file because it
 * is the row the sidebar draws most, and it takes the document's words as
 * plain values so it does not need the library to draw them.
 */
import { useId } from 'react';
import type { HeldBy } from '../../../lib/enforced';
import { RowIcon, type RowKind } from '../../shared/SheetIcons';
import styles from '../SideNav/SideNav.module.css';

/**
 * One document's row. It reads as the current page while its document is
 * open; a sub row is indented under the row before it. On the icons-only rail
 * the words are hidden and the label becomes the tooltip.
 *
 * A decision record's row may carry a mark at its end (ADR-029, the addendum
 * on the marks): a filled dot where a test holds the record, a ring where
 * review does. The mark's words are the row's description rather than part of
 * its name, so the button is still named by its label alone, the name a reader
 * hears and a test clicks.
 */
export function RailDocRow({
  label,
  number,
  kind,
  sub,
  current,
  iconsOnly,
  held,
  onOpen,
}: {
  label: string;
  number?: string;
  kind: RowKind;
  sub?: boolean;
  current: boolean;
  iconsOnly: boolean;
  held?: { by: HeldBy; text: string } | null;
  onOpen: () => void;
}) {
  const described = useId();
  const mark = held && !iconsOnly ? held : null;
  return (
    <button
      type="button"
      className={sub ? `${styles.row} ${styles.subRow}` : styles.row}
      onClick={onOpen}
      aria-current={current ? 'page' : undefined}
      title={iconsOnly ? label : undefined}
      aria-describedby={mark ? described : undefined}
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
      {mark ? (
        <>
          <span
            className={styles.heldMark}
            data-held={mark.by}
            title={mark.text}
            aria-hidden="true"
          />
          {/* Hidden, so it is no part of the button's name, and still its description. */}
          <span id={described} hidden>
            {mark.text}
          </span>
        </>
      ) : null}
    </button>
  );
}
