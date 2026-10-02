/**
 * A sidebar row that is a link out, opening in a new tab with the external
 * icon. Its own file because sections put these before and after their
 * document rows, and the row is the same wherever it sits.
 */
import { RowIcon } from '../../shared/SheetIcons';
import styles from '../SideNav/SideNav.module.css';

/** A link drawn as a row, the same icon and label rules as a doc row; opens in a new tab. */
export function RailLinkRow({
  link,
  iconsOnly,
}: {
  link: { href: string; label: string };
  iconsOnly: boolean;
}) {
  return (
    <a
      className={styles.row}
      href={link.href}
      target="_blank"
      rel="noreferrer"
      title={iconsOnly ? link.label : undefined}
    >
      <RowIcon kind="external" className={styles.icon} />
      <span className={iconsOnly ? styles.srOnly : styles.label}>{link.label}</span>
    </a>
  );
}
