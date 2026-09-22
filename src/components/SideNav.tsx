import { useEffect, useMemo, useRef } from 'react';
import {
  DocDialog,
  DOCS,
  LINKS,
  MENU_ORDER,
  MENUS,
  type DocKey,
  type DocRequest,
} from './DocsMenu';
import { NavGlyph, RowIcon } from './SheetIcons';
import { SITE_MAP, type SiteAction } from '../lib/siteMap';
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
  /** The inventory list is the view (the landing page is home since 1.0.1.0). */
  /** The landing page is what shows: the Home row reads as current. */
  homeOpen: boolean;
  inventoryOpen: boolean;
  onOpenInventory: () => void;
  adminOpen: boolean;
  onOpenAdmin: () => void;
  accountOpen: boolean;
  onOpenAccount: () => void;
  /** The signed-in address, or null. The row's label either way. */
  accountEmail: string | null;
  bidCount: number;
  onResetBids: () => void;
  build: Build;
  /**
   * The record showing, or null. App owns it because App owns the address bar
   * (ADR: A record with no address): a document is a view, and every other
   * view here is a GET parameter.
   */
  openDocKey: DocKey | null;
  /**
   * Named for the change rather than for opening, because null is a real value
   * here: the browser closes a record by taking it out of the address bar. The
   * row callback inside this component is still an open, and takes a key.
   */
  onDocChange: (key: DocKey | null) => void;
};

/**
 * The one navigation surface (ADR-013): every header menu as a headed section
 * of icon rows, then the site map's actions pinned at the foot (the inventory,
 * the account, Admin, Reset bids, the resume and the repository). The sections'
 * order and the pinned rows both come from SITE_MAP (src/lib/siteMap.ts), the
 * structure the landing page is drawn from too. Built from the same MENUS record for both shapes it takes: a
 * docked left rail that collapses to icons on wide screens, or the slide-out
 * drawer on phones and narrow windows. Owns the one doc dialog; a row stays
 * marked current while its doc is open.
 */
export function SideNav(props: SideNavProps) {
  const { docked, collapsed, drawerOpen, onDrawerClose, openDocKey, onDocChange } = props;
  const drawerRef = useRef<HTMLDialogElement>(null);

  // #region request-from-prop
  // The dialog is driven by a request rather than a bare key, because reopening
  // the same document after closing it has to read as a new request. Memoised
  // on the key, so it is one object per open: closing sets the key to null and
  // opening the same record again recomputes, which is a different object,
  // which is all "new request" ever meant.
  const request = useMemo<DocRequest | null>(
    () => (openDocKey === null ? null : { key: openDocKey }),
    [openDocKey]
  );
  // #endregion request-from-prop

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
    onDocChange(key);
  };
  // #endregion drawer-dialog

  const content = (
    <NavContent
      {...props}
      openKey={openDocKey}
      onOpenDoc={openDoc}
      onCloseDrawer={() => drawerRef.current?.close()}
    />
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
      <DocDialog request={request} onClose={() => onDocChange(null)} />
    </>
  );
}

// #region section-shell
/**
 * A sidebar section: a native `details`, closed until somebody asks for it.
 *
 * Every section works this way since 1.0.0.135, on the owner's instruction.
 * Before it, only the records index collapsed and the other ten sections were
 * always open, which meant the rail opened on about a hundred rows and the
 * reader's own section was somewhere inside them. Closed by default turns the
 * sidebar back into a table of contents: eleven headings, and the one you want
 * is one click away. The keyboard and screen-reader behaviour comes from the
 * element rather than from a reimplementation of it, which is why this is a
 * `details` and not a button and a piece of state.
 *
 * The icons-only rail is the exception, and it is the case that nearly went out
 * wrong once before (the staff review, 2026-09-03). There are no headings on
 * that rail: the rows are icons and the words are hidden, so a closed section
 * would be a triangle with nothing to read and nothing to aim at. It keeps the
 * rows.
 */
function SectionShell({
  label,
  iconsOnly,
  children,
}: {
  label: string;
  iconsOnly: boolean;
  children: React.ReactNode;
}) {
  if (iconsOnly) {
    return (
      <section className={styles.section}>
        <h2 className={styles.srOnly}>{label}</h2>
        {children}
      </section>
    );
  }
  return (
    <details
      className={styles.section}
      onToggle={(event) => {
        if (event.currentTarget.open) {
          event.currentTarget.scrollIntoView({ block: 'nearest' });
        }
      }}
    >
      <summary className={styles.sectionToggle}>
        {/* The label stays a heading inside the summary, which HTML allows and
            which keeps the eleven section names in the document outline where a
            screen reader's heading list finds them; the summary is what makes
            it a disclosure. */}
        <h2 className={styles.sectionHeading}>{label}</h2>
      </summary>
      {children}
    </details>
  );
}
// #endregion section-shell

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
  homeOpen,
  inventoryOpen,
  onOpenInventory,
  adminOpen,
  onOpenAdmin,
  accountOpen,
  onOpenAccount,
  accountEmail,
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
          title="The Yard: home"
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
          <button
            type="button"
            className={styles.toggle}
            onClick={onCloseDrawer}
            aria-label="Close"
          >
            <svg viewBox="0 0 14 14" width="14" height="14" aria-hidden="true">
              <path
                d="M2 2l10 10M12 2 2 12"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
              />
            </svg>
          </button>
        )}
      </div>

      <nav className={styles.sections} aria-label="Project documents">
        <div className={styles.scroll}>
          {/* #region rows */}
          {MENU_ORDER.map((variant) => (
            <SectionShell key={variant} label={MENUS[variant].label} iconsOnly={iconsOnly}>
              {MENUS[variant].items.map(({ key, sub }) => (
                <button
                  key={key}
                  type="button"
                  className={sub ? `${styles.row} ${styles.subRow}` : styles.row}
                  onClick={() => onOpenDoc(key)}
                  aria-current={openKey === key ? 'page' : undefined}
                  title={iconsOnly ? DOCS[key].menuLabel : undefined}
                >
                  <RowIcon kind={DOCS[key].kind} className={styles.icon} />
                  <span className={iconsOnly ? styles.srOnly : styles.label}>
                    {DOCS[key].number ? (
                      <>
                        <span className={styles.recordNumber}>{DOCS[key].number}</span>{' '}
                      </>
                    ) : null}
                    {DOCS[key].menuLabel}
                  </span>
                </button>
              ))}
              {MENUS[variant].links?.map((link) => (
                <LinkRow key={link.href} link={link} iconsOnly={iconsOnly} />
              ))}
            </SectionShell>
          ))}
          {/* #endregion rows */}
        </div>

        <div className={styles.pinned}>
          {/* #region pinned-rows */}
          {/* The site map's actions, in its order. The account row's label is
              the signed-in address when there is one, which is also how a
              visitor checks who they are without opening anything; .label
              already truncates, so a long address does not widen the rail.
              Reset bids is not in the map, because it is not a place: it
              appears after Admin only while there are bids to reset. */}
          {SITE_MAP.actions
            .filter((action) => action.inRail)
            .map((action) => (
              <PinnedRow
                key={action.key}
                action={action}
                iconsOnly={iconsOnly}
                current={
                  action.key === 'home'
                    ? homeOpen
                    : action.key === 'inventory'
                      ? inventoryOpen
                      : action.key === 'account'
                        ? accountOpen
                        : action.key === 'admin'
                          ? adminOpen
                          : false
                }
                label={action.key === 'account' ? (accountEmail ?? action.label) : action.label}
                onOpen={() => {
                  onCloseDrawer();
                  if (action.key === 'home') onHome();
                  if (action.key === 'inventory') onOpenInventory();
                  if (action.key === 'account') onOpenAccount();
                  if (action.key === 'admin') onOpenAdmin();
                }}
                after={
                  action.key === 'admin' && bidCount > 0 ? (
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
                      <span className={iconsOnly ? styles.srOnly : styles.label}>
                        Reset bids ({bidCount})
                      </span>
                    </button>
                  ) : null
                }
              />
            ))}
          {/* #endregion pinned-rows */}
        </div>
      </nav>
    </>
  );
}

/** A link drawn as a row, the same icon and label rules as a doc row; opens in a new tab. */
function LinkRow({
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

/**
 * One of the site map's actions as a pinned row: a view App opens, or a link
 * out (the resume and the repository) in a new tab.
 */
function PinnedRow({
  action,
  iconsOnly,
  current,
  label,
  onOpen,
  after,
}: {
  action: SiteAction;
  iconsOnly: boolean;
  current: boolean;
  label: string;
  onOpen: () => void;
  after: React.ReactNode;
}) {
  const inner = (
    <>
      <NavGlyph icon={action.icon} className={styles.icon} />
      <span className={iconsOnly ? styles.srOnly : styles.label}>{label}</span>
    </>
  );
  const row =
    action.key === 'resume' || action.key === 'repo' ? (
      <a
        className={styles.row}
        href={LINKS[action.key].href}
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
