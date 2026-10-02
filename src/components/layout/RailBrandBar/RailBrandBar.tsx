/**
 * The top of the sidebar: the site's name and build, which goes home, and the
 * one control beside it, collapse and expand on the docked rail or close on
 * the drawer. Its own file because it is the rail's frame, not one of its
 * rows, and it draws its own two icons.
 */
import { BrandMark } from '../BrandMark';
import styles from '../SideNav/SideNav.module.css';
import { ICON } from '../../../lib/icons';

/** The running build's version and commit, or null before the API has said. */
export type RailBuild = { version: string; commit: string } | null;

/**
 * The brand button and the rail's toggle. On the docked rail the toggle
 * collapses it to icons and back; on the drawer it is the close button.
 */
export function RailBrandBar({
  docked,
  collapsed,
  iconsOnly,
  build,
  onHome,
  onToggleCollapsed,
  onCloseDrawer,
}: {
  docked: boolean;
  collapsed: boolean;
  iconsOnly: boolean;
  build: RailBuild;
  onHome: () => void;
  onToggleCollapsed: () => void;
  onCloseDrawer: () => void;
}) {
  const versionLabel = build ? (build.version === 'dev' ? 'dev build' : `v${build.version}`) : '';

  return (
    <div className={styles.brandBlock}>
      <button
        type="button"
        className={styles.brand}
        onClick={() => {
          onCloseDrawer();
          onHome();
        }}
        title="The Yard: home"
      >
        <BrandMark size={22} className={styles.brandMark} />
        <span className={iconsOnly ? styles.srOnly : styles.brandText}>
          The Yard
          <small className={styles.brandSub}>{versionLabel || ' '}</small>
        </span>
      </button>
      {docked ? (
        <button
          type="button"
          className={styles.toggle}
          onClick={onToggleCollapsed}
          aria-label={collapsed ? 'Expand the sidebar' : 'Collapse the sidebar'}
          aria-expanded={!collapsed}
        >
          <svg viewBox="0 0 20 20" width={ICON.md} height={ICON.md} aria-hidden="true">
            <path
              d={collapsed ? 'M6 4l6 6-6 6M11 4l6 6-6 6' : 'M14 4l-6 6 6 6M9 4l-6 6 6 6'}
              fill="none"
              stroke="currentColor"
              strokeWidth={ICON.stroke}
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      ) : (
        <button type="button" className={styles.toggle} onClick={onCloseDrawer} aria-label="Close">
          <svg viewBox="0 0 14 14" width={ICON.sm} height={ICON.sm} aria-hidden="true">
            <path
              d="M2 2l10 10M12 2 2 12"
              stroke="currentColor"
              strokeWidth={ICON.stroke}
              strokeLinecap="round"
            />
          </svg>
        </button>
      )}
    </div>
  );
}
