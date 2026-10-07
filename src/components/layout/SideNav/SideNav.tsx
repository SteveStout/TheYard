/**
 * The one navigation surface (ADR: The sidebar): its two shapes, the document
 * dialog it owns, and the rows it lists. Each piece it draws sits in a folder
 * beside it, and this file names them and puts them in order:
 *   railStorage.ts      the docked rail's collapsed state, kept per browser
 *   RailBrandBar        the site's name, home, and the collapse or close button
 *   RailSectionShell    one section, a disclosure that starts closed
 *   RailDocRow          a row that opens a document
 *   RailLinkRow         a row that is a link out
 *   RailPinnedRows      the site map's actions at the foot, with Reset bids
 *   RailPinnedRow       one of those actions
 *   RailResetBidsRow    Reset bids, under Admin while there are bids
 */
import { useEffect, useMemo, useRef } from 'react';
import { DocDialog, type DocRequest } from '../../../library/DocDialog';
import { DOCS, type DocKey } from '../../../library/documents';
import { LINKS, MENUS } from '../../../library/sections';
import { SITE_GROUPS, sectionsIn } from '../../../lib/siteMap';
import { RailBrandBar, type RailBuild } from '../RailBrandBar';
import { RailSectionShell } from '../RailSectionShell';
import { RailDocRow } from '../RailDocRow';
import { RailLinkRow } from '../RailLinkRow';
import { RailPinnedRows } from '../RailPinnedRows';
import styles from './SideNav.module.css';

export { readRailCollapsed, storeRailCollapsed } from './railStorage';

/** Everything the shell hands the sidebar: its shape, which view is showing, and what each row does. */
export type SideNavProps = {
  /** At 1024px and up the panel docks beside the page; below, it is a drawer; null while a drawn page is taken over. */
  docked: boolean | null;
  /** Docked only: icons-only rail. */
  collapsed: boolean;
  /** Collapses the docked rail to icons, or expands it again. */
  onToggleCollapsed: () => void;
  /** Drawer only: useRail owns the open flag; the hamburger in the header sets it. */
  drawerOpen: boolean;
  /** Called when the drawer closes, whatever closed it. */
  onDrawerClose: () => void;
  /** Goes to the landing page. */
  onHome: () => void;
  /** The landing page is what shows: the Home row reads as current. */
  homeOpen: boolean;
  /** The inventory list is the view: the Inventory row reads as current. */
  inventoryOpen: boolean;
  /** Opens the inventory list. */
  onOpenInventory: () => void;
  /** The Admin tab is the view: the Admin row reads as current. */
  adminOpen: boolean;
  /** Opens the Admin tab. */
  onOpenAdmin: () => void;
  /** The account page is the view: the account row reads as current. */
  accountOpen: boolean;
  /** Opens the account page. */
  onOpenAccount: () => void;
  /** The signed-in address, or null. The row's label either way. */
  accountEmail: string | null;
  /** How many bids this visitor has; Reset bids shows only above zero. */
  bidCount: number;
  /** Clears this visitor's bids. */
  onResetBids: () => void;
  /** The running build, shown under the site's name. */
  build: RailBuild;
  /**
   * The record showing, or null. useAddressBar owns it because it owns the
   * address bar (ADR: A record with no address): a document is a view, and
   * every other view here is a GET parameter.
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
 * The one navigation surface (ADR: The sidebar): every header menu as a headed
 * section of icon rows, then the site map's actions pinned at the foot (the
 * inventory, the account, Admin, Reset bids, the resume and the repository).
 * The sections' order and the pinned rows both come from SITE_MAP
 * (src/lib/siteMap.ts), the structure the landing page is drawn from too.
 * Built from the same MENUS record for both shapes it takes: a docked left rail
 * that collapses to icons on wide screens, or the slide-out drawer on phones
 * and narrow windows. Owns the one doc dialog; a row stays marked current while
 * its doc is open.
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
  // The drawer is a native dialog: useRail flips drawerOpen, this mirrors it.
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
      docked={docked !== false}
      openKey={openDocKey}
      onOpenDoc={openDoc}
      onCloseDrawer={() => drawerRef.current?.close()}
    />
  );

  return (
    <>
      {/* #region shapes */}
      {docked !== false ? (
        // Not known yet (null) draws the rail too, and the stylesheet hides it below the docking line.
        <aside
          className={styles.rail}
          data-collapsed={collapsed}
          data-auto={docked === null ? '' : undefined}
          data-testid="side-rail"
        >
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
      <DocDialog request={request} onClose={() => onDocChange(null)} onOpenDoc={onDocChange} />
    </>
  );
}

/** The sidebar's props plus what SideNav itself works out: the open key and the two drawer-aware callbacks. */
type ContentProps = Omit<SideNavProps, 'docked'> & {
  /** The rail's shape is drawn: docked, or not known yet. */
  docked: boolean;
  openKey: DocKey | null;
  onOpenDoc: (key: DocKey) => void;
  onCloseDrawer: () => void;
};

/**
 * What both shapes hold: the brand bar, the sections of document rows in the
 * site map's groups, and the pinned actions at the foot. Every row closes the
 * drawer before it acts, so a phone reader lands on what they chose.
 */
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
  const iconsOnly = docked && collapsed;

  return (
    <>
      <RailBrandBar
        docked={docked}
        collapsed={collapsed}
        iconsOnly={iconsOnly}
        build={build}
        onHome={onHome}
        onToggleCollapsed={onToggleCollapsed}
        onCloseDrawer={onCloseDrawer}
      />

      <nav className={styles.sections} aria-label="Project documents">
        <div className={styles.scroll}>
          {/* #region rows */}
          {/* The site map's groups, each heading above its sections; the
              sections inside a group keep MENU_ORDER's order. */}
          {SITE_GROUPS.map((group) => (
            <div key={group.key} className={styles.group} data-testid={`rail-group-${group.key}`}>
              <p className={iconsOnly ? styles.srOnly : styles.groupTitle}>{group.label}</p>
              {sectionsIn(group.key).map(({ menu: variant }) => (
                <RailSectionShell key={variant} label={MENUS[variant].label} iconsOnly={iconsOnly}>
                  {MENUS[variant].lead?.map((link) => (
                    <RailLinkRow key={link.href} link={link} iconsOnly={iconsOnly} />
                  ))}
                  {MENUS[variant].items.map(({ key, sub }) => (
                    <RailDocRow
                      key={key}
                      label={DOCS[key].menuLabel}
                      number={DOCS[key].number}
                      kind={DOCS[key].kind}
                      sub={sub}
                      current={openKey === key}
                      iconsOnly={iconsOnly}
                      onOpen={() => onOpenDoc(key)}
                    />
                  ))}
                  {MENUS[variant].links?.map((link) => (
                    <RailLinkRow key={link.href} link={link} iconsOnly={iconsOnly} />
                  ))}
                </RailSectionShell>
              ))}
            </div>
          ))}
          {/* #endregion rows */}
        </div>

        <RailPinnedRows
          iconsOnly={iconsOnly}
          views={{
            home: { current: homeOpen, open: onHome },
            inventory: { current: inventoryOpen, open: onOpenInventory },
            account: { current: accountOpen, open: onOpenAccount },
            admin: { current: adminOpen, open: onOpenAdmin },
          }}
          links={{ resume: LINKS.resume.href, repo: LINKS.repo.href }}
          accountEmail={accountEmail}
          bidCount={bidCount}
          onResetBids={onResetBids}
          onCloseDrawer={onCloseDrawer}
        />
      </nav>
    </>
  );
}
