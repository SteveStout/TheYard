/**
 * Does:      Draws the frame every page sits in: the skip link, the sidebar, the phone's header, the store bar, main, the footer.
 * Does not:  Know anything about vehicles, or choose which view goes inside main (App.tsx does).
 * Used by:   App.tsx.
 */
import type { ReactNode } from 'react';
import { SideNav } from '../components/layout/SideNav';
import { StoreBar } from '../components/layout/StoreBar';
import { Background } from '../components/layout/Background';
import type { DocKey } from '../library/documents';
import { Footer } from './Footer';
import { Header } from './Header';
import { MAIN_ID, type Navigation } from './hooks/useNavigation';
import type { Rail } from './hooks/useRail';
import type { RunningBuild } from './hooks/useRunningBuild';
import styles from './App.module.css';

/**
 * What the frame's stylesheet reads off data-rail: docked open or collapsed, a drawer, or "auto" while a page drawn at
 * build time is taken over and the docking line is not known yet (src/styles/drawn-page.css then shows the frame the
 * window wants).
 */
function railShape(rail: Rail): 'auto' | 'drawer' | 'collapsed' | 'open' {
  if (rail.docked === null) return 'auto';
  if (!rail.docked) return 'drawer';
  return rail.collapsed ? 'collapsed' : 'open';
}

export function Shell({
  rail,
  go,
  views,
  accountEmail,
  bidCount,
  onResetBids,
  build,
  children,
}: {
  rail: Rail;
  go: Navigation;
  /** Which of the rail's own rows read as current. */
  views: {
    homeShowing: boolean;
    listShowing: boolean;
    adminOpen: boolean;
    accountOpen: boolean;
    openDocKey: DocKey | null;
  };
  accountEmail: string | null;
  bidCount: number;
  onResetBids: () => void;
  build: RunningBuild;
  children: ReactNode;
}) {
  return (
    <div className={styles.app} data-rail={railShape(rail)}>
      {/* #region skip-link */}
      {/* First in the tab order on purpose. The docked rail is around thirty
          buttons, and without this a keyboard user tabs every one of them to
          reach the grid, on every page view. Hidden until it has focus. */}
      <a className={styles.skipLink} href={`#${MAIN_ID}`}>
        Skip to content
      </a>
      {/* #endregion skip-link */}
      {/* The one navigation surface (ADR-013): a docked rail on wide screens,
          a drawer behind the header's hamburger below 1024px. */}
      <SideNav
        docked={rail.docked}
        collapsed={rail.collapsed}
        onToggleCollapsed={rail.toggleCollapsed}
        drawerOpen={rail.drawerOpen}
        onDrawerClose={rail.closeDrawer}
        onHome={go.goHome}
        homeOpen={views.homeShowing}
        inventoryOpen={views.listShowing}
        onOpenInventory={go.openInventory}
        adminOpen={views.adminOpen}
        onOpenAdmin={go.openAdmin}
        accountOpen={views.accountOpen}
        onOpenAccount={go.openAccount}
        accountEmail={accountEmail}
        bidCount={bidCount}
        onResetBids={onResetBids}
        build={build}
        openDocKey={views.openDocKey}
        onDocChange={go.openDocument}
      />

      <div className={styles.page}>
        <Background />
        {/* #region header-below-dock */}
        {/* Below the docking line the header carries the brand, Reset bids,
            and the hamburger; the docked rail makes it redundant above it.
            What it draws is in Header.tsx. While a page drawn at build time is
            taken over, the line is not known yet, so both the header and the
            rail are drawn and the stylesheet shows the one the window wants
            (data-rail="auto", src/styles/drawn-page.css). */}
        {rail.docked !== true && (
          <Header
            onHome={go.goHome}
            bidCount={bidCount}
            onResetBids={onResetBids}
            drawerOpen={rail.drawerOpen}
            onOpenDrawer={rail.openDrawer}
          />
        )}
        {/* #endregion header-below-dock */}

        {/* #region store-bar */}
        {/* The store toggle (SQL or Cosmos DB), at the top of every view on
            every width (ADR-066). Above main so it is not part of the view
            that announces itself, and below the phone header so the
            hamburger keeps its corner. */}
        <StoreBar />
        {/* #endregion store-bar */}

        <main className={styles.main} id={MAIN_ID} tabIndex={-1}>
          <p className={styles.srOnly} role="status" data-testid="view-announcement">
            {go.announcement}
          </p>
          {children}
        </main>

        <Footer build={build} />
      </div>
    </div>
  );
}
