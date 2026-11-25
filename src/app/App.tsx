/**
 * Does:      Joins the pieces of the site together, picks which view shows inside the frame, and says which code loads late.
 * Does not:  Hold state or rules of its own: what the app knows is in hooks/, one file per thing, named for what it gives back.
 * Used by:   TheYard.tsx.
 */
import { lazy, Suspense, useEffect, useState } from 'react';
import { useFirstLoad, useInTheBrowser } from '../hooks/useFirstLoad';
import { useNow } from '../hooks/useNow';
import { holdCode, pageOpenReport, reportPath, sessionOrNull } from '../lib/links';
import { screenOf } from '../lib/reviewMirror';
import { REVIEW_REPORT_PATH } from '../lib/reviewStops';
import { RECOMMENDATIONS_REPORT_PATH } from '../lib/recommendationsPage';
import { ABOUT_REPORT_PATH } from '../lib/aboutPage';
import { prefetchWhenIdle } from '../lib/prefetch';
import { AccountPanel } from '../components/account/AccountPanel';
import { Landing } from '../components/landing/Landing';
import { BlogPage } from '../components/blog/BlogPage';
import { ReviewPage } from '../components/review/ReviewPage';
import { RecommendationsPage } from '../components/recommendations/RecommendationsPage';
import { StackPage } from '../components/landing/StackPage';
import { AboutPage } from '../components/about/AboutPage';
import { VehicleDetail } from '../components/vehicle/VehicleDetail';
import { useAccount } from './hooks/useAccount';
import { useAddressBar } from './hooks/useAddressBar';
import { useInventory } from './hooks/useInventory';
import { useNavigation } from './hooks/useNavigation';
import { useOpenVehicle } from './hooks/useOpenVehicle';
import { useRail } from './hooks/useRail';
import { useRunningBuild } from './hooks/useRunningBuild';
import { InventoryLoading, InventoryUnreachable, InventoryView } from './InventoryView';
import { Shell } from './Shell';
import styles from './App.module.css';
import { request } from '../lib/request';

// #region admin-on-demand
// The Admin tab is the biggest view in the app, and few visitors open it.
// lazy() splits it into its own file that the browser downloads only when
// ?view=admin opens, so the landing page does not pay for it.
const loadAdminPanel = () => import('../components/admin/AdminPanel/AdminPanel');
const AdminPanel = lazy(() =>
  loadAdminPanel().then((module) => ({
    default: module.AdminPanel,
  }))
);
// #endregion admin-on-demand

export default function App() {
  // #region table-of-contents
  const address = useAddressBar(); // which view the address names; the only code that writes it
  const { account, changeAccount } = useAccount(); // who is signed in
  const inventory = useInventory(address, account.email); // the vehicle list, with this visitor's bids on it
  const vehicle = useOpenVehicle(address, inventory); // the vehicle on screen
  const rail = useRail(address.reviewOpen); // the sidebar: docked, collapsed, or a drawer
  const go = useNavigation(address, inventory.loadState); // open and close views, move focus, announce
  const build = useRunningBuild(); // the version line, asked of the API
  const now = useNow(1000, useFirstLoad()?.nowMs); // the clock every countdown reads
  const inBrowser = useInTheBrowser(); // Admin and the account page draw only here, never on a server
  // #endregion table-of-contents

  // #region fetch-ahead
  // The two chunks a click most often waits on, fetched once the first page has
  // loaded and the browser is idle: the renderer every document needs and the
  // Admin tab. Nothing here is on the first page's bytes, and a reader who asked
  // to save data, or is on 2G, gets none of it (src/lib/prefetch.ts). A reader who
  // came for the blog gets none of it either: a post is drawn on save and needs no
  // renderer, and the 200 KB the two weigh are the blog route's budget twice over.
  const [cameForTheBlog] = useState(() => address.blogOpen);
  useEffect(() => {
    if (!cameForTheBlog) return prefetchWhenIdle([() => import('../lib/markdown'), loadAdminPanel]);
  }, [cameForTheBlog]);
  // #endregion fetch-ahead

  // #region tagged-open
  // A link the operator sent carries ?r= and a code; the page is served by the rendering
  // service, so the API hears of the open only from this one report, sent once, on load
  // (ADR: Tagged links, who opened what). The code is all it carries.
  // The address is read during the first render, because the address-bar mirror above rewrites
  // it in its own effect, before this one would run, and the code goes with the rewrite. The review
  // route is its own page view, kept under its own path (ADR: Ten minutes, seven stops); About Steven keeps /about's.
  const [openReport] = useState(() =>
    typeof window === 'undefined'
      ? null
      : pageOpenReport(
          window.location.search,
          reportPath(
            window.location.search,
            address.reviewOpen
              ? REVIEW_REPORT_PATH
              : address.recommendationsOpen
                ? RECOMMENDATIONS_REPORT_PATH
                : address.aboutOpen
                  ? ABOUT_REPORT_PATH
                  : '/'
          )
        )
  );
  // The code is held for the visit as well, so the "Where next" links carry it (ladder v7b).
  useState(() =>
    typeof window === 'undefined' ? null : holdCode(window.location.search, sessionOrNull())
  );
  useEffect(() => {
    if (openReport !== null)
      void request(openReport, undefined, { background: true }).catch(() => {});
  }, [openReport]);
  // #endregion tagged-open

  // One view at a time, in this order: Admin, Account, the blog, the review route, the recommendations, the stack,
  // About Steven, the landing page, then the inventory (loading, unreachable, a vehicle, or the list).
  return (
    <Shell
      rail={rail}
      go={go}
      views={address}
      accountEmail={account.email}
      bidCount={inventory.bidCount}
      onResetBids={inventory.confirmResetBids}
      build={build}
    >
      {address.adminOpen && !inBrowser ? (
        <p className={styles.adminLoading}>Reading the machines...</p>
      ) : address.adminOpen ? (
        <Suspense fallback={<p className={styles.adminLoading}>Reading the machines...</p>}>
          <AdminPanel
            onBack={go.closeAdmin}
            backTo={go.backTo()}
            card={address.adminCard}
            pin={address.adminPin}
            cardAsked={address.cardAsked}
            onOpenCard={go.openAdminCard}
            onPin={address.setAdminPin}
            signedIn={account.signedIn}
            onOpenAccount={go.openAccount}
          />
        </Suspense>
      ) : address.accountOpen && !inBrowser ? (
        <p className={styles.adminLoading}>Reading your account...</p>
      ) : address.accountOpen ? (
        <AccountPanel
          account={account}
          onAccountChange={changeAccount}
          onOpenVehicle={go.openVehicleById}
          onBack={go.closeAccount}
          backTo={go.backTo()}
          resetToken={address.resetToken}
        />
      ) : address.blogOpen ? (
        <BlogPage
          slug={address.blogPost}
          query={address.pageQuery}
          onOpen={go.openBlog}
          onMove={go.moveBlog}
          onAllPosts={go.closeBlogPost}
        />
      ) : address.reviewOpen ? (
        <ReviewPage
          screen={screenOf(address.pageQuery)}
          now={now}
          onOpenDoc={go.openDocument}
          onOpenBlog={go.openBlog}
          onOpenSidebar={rail.open}
        />
      ) : address.recommendationsOpen ? (
        <RecommendationsPage />
      ) : address.stackOpen ? (
        <StackPage />
      ) : address.aboutOpen ? (
        <AboutPage />
      ) : !address.inventoryOpen ? (
        <Landing
          accountLabel={account.email ?? 'Sign in'}
          onOpenInventory={go.openInventory}
          onOpenAccount={go.openAccount}
          onOpenAdmin={go.openAdmin}
          onOpenDoc={go.openDocument}
          onOpenBlog={go.openBlog}
          onOpenReview={go.openReview}
        />
      ) : inventory.loadState === 'loading' ? (
        <InventoryLoading />
      ) : inventory.loadState === 'error' ? (
        <InventoryUnreachable onRetry={inventory.retryFirstLoad} />
      ) : vehicle.selected ? (
        <VehicleDetail
          key={vehicle.selected.id}
          vehicle={vehicle.selected}
          now={now}
          onBack={go.backToInventory}
          isHighBidder={vehicle.isHighBidder}
          isOutbid={vehicle.isOutbid}
          wonBuyNow={vehicle.wonBuyNow}
          signedIn={account.signedIn}
          onOpenAccount={go.openAccount}
          onPlaceBid={vehicle.onPlaceBid}
          onBuyNow={vehicle.onBuyNow}
        />
      ) : (
        <InventoryView
          inventory={inventory}
          filters={address.filters}
          onFiltersChange={address.patchFilters}
          onClearFilters={address.clearFilters}
          sort={address.sort}
          onSortChange={address.setSort}
          now={now}
          onSelect={go.openVehicle}
        />
      )}
    </Shell>
  );
}
