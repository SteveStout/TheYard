/**
 * Does:      Draws the header a phone or narrow window shows: the bolt and The Yard, Reset bids, the resume, and the menu button.
 * Does not:  Show on a wide screen (the docked rail carries all of this there), or know what the menu holds.
 * Used by:   Shell.tsx.
 */
import { ICON } from '../lib/icons';
import { LINKS } from '../library/sections';
import { BrandMark } from '../components/layout/BrandMark';
import { NavGlyph } from '../components/shared/SheetIcons';
import styles from './App.module.css';

export function Header({
  onHome,
  bidCount,
  onResetBids,
  drawerOpen,
  onOpenDrawer,
}: {
  onHome: () => void;
  bidCount: number;
  onResetBids: () => void;
  drawerOpen: boolean;
  onOpenDrawer: () => void;
}) {
  return (
    <header className={styles.header} data-frame="header">
      <div className={styles.headerInner}>
        <button type="button" className={styles.brand} onClick={onHome}>
          <BrandMark size={18} className={styles.brandMark} />
          The Yard
          <span className={styles.brandSub}>Vehicle Auctions</span>
        </button>
        <div className={styles.headerActions}>
          {bidCount > 0 && (
            <button type="button" className={styles.resetBids} onClick={onResetBids}>
              Reset bids ({bidCount})
            </button>
          )}
          {/* The resume as an icon here; the docked rail has its own row for it. */}
          <a
            className={styles.headerIcon}
            href={LINKS.resume.href}
            target="_blank"
            rel="noreferrer"
            aria-label={LINKS.resume.label}
            title={LINKS.resume.label}
            data-testid="header-resume"
          >
            <NavGlyph icon="resume" size={22} />
          </a>
          <button
            type="button"
            className={styles.hamburger}
            aria-label="Menu"
            aria-haspopup="dialog"
            aria-expanded={drawerOpen}
            onClick={onOpenDrawer}
          >
            <svg viewBox="0 0 20 20" width={ICON.md} height={ICON.md} aria-hidden="true">
              <path
                d="M3 5h14M3 10h14M3 15h14"
                stroke="currentColor"
                strokeWidth={ICON.stroke}
                strokeLinecap="round"
              />
            </svg>
          </button>
        </div>
      </div>
    </header>
  );
}
