import { useEffect, useRef, useState } from 'react';
import {
  DocDialog,
  DOCS,
  LINKS,
  MENU_ORDER,
  MENUS,
  type DocKey,
  type DocRequest,
} from './DocsMenu';
import { RowIcon } from './SheetIcons';
import { BrandMark } from './BrandMark';
import styles from './SideNav.module.css';

/** The rail's collapsed state survives reloads per browser; a missing or blocked store means open. */
const RAIL_KEY = 'theyard.rail';

export function readRailCollapsed(): boolean {
  try {
    return window.localStorage.getItem(RAIL_KEY) === 'collapsed';
  } catch {
    return false;
  }
}

export function storeRailCollapsed(collapsed: boolean): void {
  try {
    window.localStorage.setItem(RAIL_KEY, collapsed ? 'collapsed' : 'open');
  } catch {
    // Private mode or a blocked store: the rail simply starts open next time.
  }
}

type Build = { version: string; commit: string } | null;

export type SideNavProps = {
  /** At 1024px and up the panel docks beside the page; below, it is a drawer. */
  docked: boolean;
  /** Docked only: icons-only rail. */
  collapsed: boolean;
  onToggleCollapsed: () => void;
  /** Drawer only: App owns the open flag; the hamburger in the header sets it. */
  drawerOpen: boolean;
  onDrawerClose: () => void;
  onHome: () => void;
  adminOpen: boolean;
  onOpenAdmin: () => void;
  bidCount: number;
  onResetBids: () => void;
  build: Build;
};

/**
 * The one navigation surface (ADR-013): every header menu as a headed section
 * of icon rows, then Admin, Reset bids, the resume, and the repository,
 * pinned. Built from the same MENUS record for both shapes it takes: a
 * docked left rail that collapses to icons on wide screens, or the slide-out
 * drawer on phones and narrow windows. Owns the one doc dialog; a row stays
 * marked current while its doc is open.
 */
export function SideNav(props: SideNavProps) {
  const { docked, collapsed, drawerOpen, onDrawerClose } = props;
  const drawerRef = useRef<HTMLDialogElement>(null);
  const [request, setRequest] = useState<DocRequest | null>(null);
  const [openKey, setOpenKey] = useState<DocKey | null>(null);

  // #region drawer-dialog
  // The drawer is a native dialog: App flips drawerOpen, this mirrors it.
  useEffect(() => {
    const drawer = drawerRef.current;
    if (!drawer) return;
    if (drawerOpen && !drawer.open) drawer.showModal();
    if (!drawerOpen && drawer.open) drawer.close();
  }, [drawerOpen, docked]);

  const openDoc = (key: DocKey) => {
    drawerRef.current?.close();
    setOpenKey(key);
    setRequest((prev) => ({ key, nonce: (prev?.nonce ?? 0) + 1 }));
  };
  // #endregion drawer-dialog

  const content = (
    <NavContent {...props} openKey={openKey} onOpenDoc={openDoc} onCloseDrawer={() => drawerRef.current?.close()} />
  );

  return (
    <>
      {/* #region shapes */}
      {docked ? (
        <aside className={styles.rail} data-collapsed={collapsed} data-testid="side-rail">
          {content}
        </aside>
      ) : (
        <dialog
          ref={drawerRef}
          className={styles.drawer}
          aria-label="Menu"
          onClose={onDrawerClose}
          onClick={(event) => {
            // Native dialog: a click on the backdrop targets the dialog itself.
            if (event.target === drawerRef.current) drawerRef.current?.close();
          }}
        >
          {content}
        </dialog>
      )}
      {/* #endregion shapes */}
      <DocDialog request={request} onClose={() => setOpenKey(null)} />
    </>
  );
}

type ContentProps = SideNavProps & {
  openKey: DocKey | null;
  onOpenDoc: (key: DocKey) => void;
  onCloseDrawer: () => void;
};

function NavContent({
  docked,
  collapsed,
  onToggleCollapsed,
  onHome,
  adminOpen,
  onOpenAdmin,
  bidCount,
  onResetBids,
  build,
  openKey,
  onOpenDoc,
  onCloseDrawer,
}: ContentProps) {
  const versionLabel = build ? (build.version === 'dev' ? 'dev build' : `v${build.version}`) : '';
  const iconsOnly = docked && collapsed;

  return (
    <>
      <div className={styles.brandBlock}>
        <button
          type="button"
          className={styles.brand}
          onClick={() => {
            onCloseDrawer();
            onHome();
          }}
          title="The Yard: back to the inventory"
        >
          <BrandMark size={22} className={styles.brandMark} />
          <span className={iconsOnly ? styles.srOnly : styles.brandText}>
            The Yard
            {versionLabel && <small className={styles.brandSub}>{versionLabel}</small>}
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
            <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true">
              <path
                d={collapsed ? 'M6 4l6 6-6 6M11 4l6 6-6 6' : 'M14 4l-6 6 6 6M9 4l-6 6 6 6'}
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </button>
        ) : (
          <button type="button" className={styles.toggle} onClick={onCloseDrawer} aria-label="Close">
            <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
              <path d="M2 2l10 10M12 2 2 12" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
          </button>
        )}
      </div>

      <nav className={styles.sections} aria-label="Project documents">
        <div className={styles.scroll}>
          {MENU_ORDER.map((variant) => (
            <section key={variant} className={styles.section}>
              <h2 className={iconsOnly ? styles.srOnly : styles.sectionTitle}>{MENUS[variant].label}</h2>
              {MENUS[variant].items.map(({ key, sub }) => (
                <button
                  key={key}
                  type="button"
                  className={sub ? `${styles.row} ${styles.subRow}` : styles.row}
                  onClick={() => onOpenDoc(key)}
                  aria-current={openKey === key ? 'true' : undefined}
                  title={iconsOnly ? DOCS[key].menuLabel : undefined}
                >
                  <RowIcon kind={DOCS[key].kind} className={styles.icon} />
                  <span className={iconsOnly ? styles.srOnly : styles.label}>{DOCS[key].menuLabel}</span>
                </button>
              ))}
              {variant === 'cicd' && <LinkRow link={LINKS.ciRuns} iconsOnly={iconsOnly} />}
            </section>
          ))}
        </div>

        <div className={styles.pinned}>
          <button
            type="button"
            className={styles.row}
            onClick={() => {
              onCloseDrawer();
              onOpenAdmin();
            }}
            aria-current={adminOpen ? 'page' : undefined}
            title={iconsOnly ? 'Admin' : undefined}
          >
            <RowIcon kind="admin" className={styles.icon} />
            <span className={iconsOnly ? styles.srOnly : styles.label}>Admin</span>
          </button>
          {bidCount > 0 && (
            <button
              type="button"
              className={styles.row}
              onClick={() => {
                onCloseDrawer();
                onResetBids();
              }}
              title={iconsOnly ? `Reset bids (${bidCount})` : undefined}
            >
              <RowIcon kind="reset" className={styles.icon} />
              <span className={iconsOnly ? styles.srOnly : styles.label}>Reset bids ({bidCount})</span>
            </button>
          )}
          <LinkRow link={LINKS.resume} iconsOnly={iconsOnly} />
          <LinkRow link={LINKS.repo} iconsOnly={iconsOnly} />
        </div>
      </nav>
    </>
  );
}

/** A link drawn as a row, the same icon and label rules as a doc row; opens in a new tab. */
function LinkRow({ link, iconsOnly }: { link: { href: string; label: string }; iconsOnly: boolean }) {
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
