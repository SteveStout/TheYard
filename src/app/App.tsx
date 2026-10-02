/**
 * Does:      Joins the pieces of the site together, picks which view shows inside the frame, and says which code loads late.
 * Does not:  Hold state or rules of its own: what the app knows is in hooks/, one file per thing, named for what it gives back.
 * Used by:   mount.tsx.
 */
import { lazy, Suspense, useEffect } from 'react';
import { useNow } from '../hooks/useNow';
import { prefetchWhenIdle } from '../lib/prefetch';
import { AccountPanel } from '../components/account/AccountPanel';
import { Landing } from '../components/landing/Landing';
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
  const rail = useRail(); // the sidebar: docked, collapsed, or a drawer
  const go = useNavigation(address, inventory.loadState); // open and close views, move focus, announce
  const build = useRunningBuild(); // the version line, asked of the API
  const now = useNow(); // the clock every countdown reads
  // #endregion table-of-contents

  // #region fetch-ahead
  // The two chunks a click most often waits on, fetched once the first page has
  // loaded and the browser is idle: the renderer every document needs and the
  // Admin tab. Nothing here is on the first page's bytes, and a reader who asked
  // to save data, or is on 2G, gets none of it (src/lib/prefetch.ts).
  useEffect(() => prefetchWhenIdle([() => import('../lib/markdown'), loadAdminPanel]), []);
  // #endregion fetch-ahead

  // One view at a time, in this order: Admin, Account, the landing page, then
  // the inventory (loading, unreachable, a vehicle, or the list).
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
      {address.adminOpen ? (
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
      ) : address.accountOpen ? (
        <AccountPanel
          account={account}
          onAccountChange={changeAccount}
          onOpenVehicle={go.openVehicleById}
          onBack={go.closeAccount}
          backTo={go.backTo()}
          resetToken={address.resetToken}
        />
      ) : !address.inventoryOpen ? (
        <Landing
          accountLabel={account.email ?? 'Sign in'}
          onOpenInventory={go.openInventory}
          onOpenAccount={go.openAccount}
          onOpenAdmin={go.openAdmin}
          onOpenDoc={go.openDocument}
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
