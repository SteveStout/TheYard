/**
 * One of the site map's actions pinned at the foot of the sidebar: a view the
 * app opens, or a link out in a new tab when the caller hands it an address.
 * Its own file because the pinned rows draw from the site map, not from the
 * document sections, and read as current by view rather than by document.
 */
import type { ReactNode } from 'react';
import { NavGlyph } from '../../shared/SheetIcons';
import type { SiteAction } from '../../../lib/siteMap';
import styles from '../SideNav/SideNav.module.css';

/**
 * One of the site map's actions as a pinned row: a view the app opens, or a link
 * out (the resume and the repository) in a new tab when `href` is given. Whatever
 * `after` holds is drawn straight under the row.
 */
export function RailPinnedRow({
  action,
  iconsOnly,
  current,
  label,
  href,
  onOpen,
  after,
}: {
  action: SiteAction;
  iconsOnly: boolean;
  current: boolean;
  label: string;
  href?: string;
  onOpen: () => void;
  after: ReactNode;
}) {
  const inner = (
    <>
      <NavGlyph icon={action.icon} className={styles.icon} />
      <span className={iconsOnly ? styles.srOnly : styles.label}>{label}</span>
    </>
  );
  const row =
    href !== undefined ? (
      <a
        className={styles.row}
        href={href}
        target="_blank"
        rel="noreferrer"
        title={iconsOnly ? label : undefined}
      >
        {inner}
      </a>
    ) : (
      <button
        type="button"
        className={styles.row}
        onClick={onOpen}
        aria-current={current ? 'page' : undefined}
        title={iconsOnly ? label : undefined}
      >
        {inner}
      </button>
    );
  return (
    <>
      {row}
      {after}
    </>
  );
}
