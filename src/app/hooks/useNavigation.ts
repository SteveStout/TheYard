/**
 * Does:      Opens and closes each view (home, the inventory, a vehicle, Admin, Account, a document), moves focus to what opened, and says it to screen readers.
 * Does not:  Write the address bar itself (it asks useAddressBar.ts), fetch the list, or draw anything.
 * Used by:   App.tsx, Shell.tsx.
 */
import { useEffect, useRef } from 'react';
import { fetchVehicleById } from '../../lib/data';
import type { Vehicle } from '../../lib/types';
import { filtersToSearchParams, opensInventory } from '../../lib/inventory';
import type { CardSlug } from '../../lib/workbench';
import { docSlug } from '../../library/addresses';
import type { DocKey } from '../../library/documents';
import type { Address, BackTo } from './useAddressBar';
import type { LoadState } from './useInventory';

export type Navigation = ReturnType<typeof useNavigation>;

/** The id of the page's <main>: the skip link's target, and where focus goes when the view changes. */
export const MAIN_ID = 'main-content';

export function useNavigation(address: Address, loadState: LoadState) {
  const {
    filters,
    sort,
    selectedVehicle,
    setSelectedVehicle,
    adminOpen,
    setAdminOpen,
    adminCard,
    setAdminCard,
    adminPin,
    setAdminPin,
    cardAsked,
    setCardAsked,
    accountOpen,
    setAccountOpen,
    inventoryOpen,
    setInventoryOpen,
    openDocKey,
    setOpenDocKey,
  } = address;

  // #region focus
  // A single-page app swaps the view without changing the document, so the
  // browser has nothing to move focus to. Open a vehicle from a tile and the
  // tile is gone: focus falls to <body>, the next Tab starts from the top of
  // the page, and a screen reader announces nothing at all. Moving focus to
  // the region that just changed is the standard repair, and it is why <main>
  // carries tabIndex={-1}: focusable by script, never by Tab. It is found by
  // its id (MAIN_ID, which Shell.tsx gives it), the same id the skip link names.
  const viewKey = adminOpen
    ? 'admin'
    : accountOpen
      ? 'account'
      : selectedVehicle
        ? `vehicle:${selectedVehicle.id}`
        : 'list';
  const lastView = useRef(viewKey);
  useEffect(() => {
    if (lastView.current === viewKey) return;
    const arrivedByItself = address.deepLinkPending.current;
    lastView.current = viewKey;
    // A deep-linked vehicle resolving is data arriving, not a view the visitor
    // chose. Moving focus for it yanks a screen reader's cursor mid-sentence
    // seconds into the page, and pulls a keyboard user off the skip link.
    if (arrivedByItself) return;
    // preventScroll because the effect below already decides where the page
    // sits; without it the two fight and the detail opens part-scrolled.
    document.getElementById(MAIN_ID)?.focus({ preventScroll: true });
  }, [viewKey, address.deepLinkPending]);
  // #endregion focus

  // #region history
  // Open the detail at the top; restore the list scroll position on back.
  const listScrollY = useRef(0);
  const openVehicle = (vehicle: Vehicle) => {
    listScrollY.current = window.scrollY;
    // Real GET navigation: push a history entry so the browser's Back works.
    const params = filtersToSearchParams(filters, sort);
    params.set('vehicle', vehicle.id);
    address.push({ viaTile: true }, params);
    setInventoryOpen(true);
    setSelectedVehicle(vehicle);
  };
  /** From the account page's bid list: open that vehicle's detail view. */
  const openVehicleById = (vehicleId: string) => {
    void fetchVehicleById(vehicleId).then((vehicle) => {
      if (!vehicle) return;
      setAccountOpen(false);
      openVehicle(vehicle);
    });
  };
  /** What the entry that opened this view recorded, read when the view draws its back button. */
  const backTo = (): BackTo => (address.entry()?.from === 'home' ? 'home' : 'inventory');
  const backToInventory = () => {
    // If we pushed this entry, going back keeps history clean; a deep-linked
    // visit has no list entry behind it, so just swap the URL in place.
    if (address.entry()?.viaTile) {
      address.stepBack();
      return;
    }
    setSelectedVehicle(null);
    setInventoryOpen(true);
    address.replaceQuery(filtersToSearchParams(filters, sort));
  };
  // #endregion history

  useEffect(() => {
    // Every view except the list opens at the top; the list returns to where
    // the reader left it. Admin counts as a view switch too.
    window.scrollTo(
      0,
      selectedVehicle || adminOpen || accountOpen || !inventoryOpen ? 0 : listScrollY.current
    );
  }, [selectedVehicle, adminOpen, accountOpen, inventoryOpen]);

  // #region open-inventory
  // The same shape as Admin and Account: a view at its own address, pushed so
  // Back returns to the landing page it was opened from.
  const openInventory = () => {
    const params = filtersToSearchParams(filters, sort);
    if (!opensInventory(params)) params.set('view', 'inventory');
    address.push({ viaInventory: true }, params);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(false);
    setInventoryOpen(true);
  };
  // #endregion open-inventory

  // Where Back goes is stored in the history entry. Opened from the landing
  // page, the button says "Back to home"; opened from the inventory, or
  // straight from its address, it says inventory.
  const openedFrom = (): BackTo => (inventoryOpen ? 'inventory' : 'home');
  const openAdmin = () => {
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'admin');
    address.push({ viaAdmin: true, from: openedFrom() }, params);
    setSelectedVehicle(null);
    setAccountOpen(false);
    setAdminCard('health');
    setAdminPin(null);
    setCardAsked(null);
    setAdminOpen(true);
  };
  /**
   * A card on the workbench is a place, so opening one pushes an entry and Back
   * returns to the card before it. The entry records benchDepth (how many cards
   * deep it is), so the view's own back button leaves the Admin tab in one step
   * however many cards were opened.
   */
  const openAdminCard = (slug: CardSlug) => {
    if (slug === adminCard && cardAsked === null) return;
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'admin');
    if (slug !== 'health') params.set('card', slug);
    if (adminPin !== null) params.set('pin', adminPin);
    const held = address.entry() ?? {};
    address.push(
      { viaAdmin: held.viaAdmin === true, from: held.from, benchDepth: (held.benchDepth ?? 0) + 1 },
      params
    );
    setAdminCard(slug);
    setCardAsked(null);
  };

  // #region open-document
  // A decision record is a view with its own address (?doc=...), the same shape
  // as Admin and Account, so a record can be sent to someone as a link (ADR-057).
  const openDocument = (key: DocKey | null) => {
    if (key !== null) {
      const params = filtersToSearchParams(filters, sort);
      params.set('doc', docSlug(key));
      // One history entry for "a record is open", however many records are
      // opened while it is, so Escape closes the dialog instead of stepping
      // back through each record. The dialog is modal, so today the replace
      // branch is unreachable; it guards a future non-modal dialog.
      if (openDocKey === null) {
        address.push({ viaDoc: true }, params);
      } else {
        address.replace({ viaDoc: true }, params);
      }
      setOpenDocKey(key);
      return;
    }
    // Closed by Escape, the X, or the backdrop. If we pushed the entry that
    // opened it, going back keeps history clean and makes Back and Escape
    // agree; a visitor who arrived on the link has no entry behind it, so the
    // parameter is swapped out in place instead of leaving the site.
    if (address.entry()?.viaDoc) {
      address.stepBack();
      return;
    }
    setOpenDocKey(null);
  };
  // #endregion open-document

  // #region open-account
  // The same shape as Admin, because it is the same kind of thing: one more
  // view at its own ?view= value, pushed so Back closes it.
  const openAccount = () => {
    const params = filtersToSearchParams(filters, sort);
    params.set('view', 'account');
    address.push({ viaAccount: true, from: openedFrom() }, params);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(true);
  };
  const closeAccount = () => {
    if (address.entry()?.viaAccount) {
      address.stepBack();
      return;
    }
    setAccountOpen(false);
    setInventoryOpen(true);
    address.replaceQuery(filtersToSearchParams(filters, sort));
  };
  // #endregion open-account
  // Leave Admin in one step, going back past every workbench card opened.
  const closeAdmin = () => {
    const held = address.entry();
    if (held?.viaAdmin) {
      address.stepBack((held.benchDepth ?? 0) + 1);
      return;
    }
    setAdminOpen(false);
    setInventoryOpen(true);
    address.replaceQuery(filtersToSearchParams(filters, sort));
  };

  /**
   * The brand button, in the rail and the phone header, always goes to the
   * landing page. Pushed, so Back returns to the view it left.
   */
  const goHome = () => {
    address.push({ viaHome: true }, null);
    setSelectedVehicle(null);
    setAdminOpen(false);
    setAccountOpen(false);
    setInventoryOpen(false);
  };

  // #region announcement
  // Focus says where you are; this says what you arrived at. Moving focus to a
  // container is announced inconsistently across screen readers, so the view's
  // name is stated outright. The match count is deliberately not repeated here:
  // the filter bar's own status line already owns that sentence, and two live
  // regions saying the same thing is worse than one saying it once.
  const announcement = adminOpen
    ? 'Admin panel'
    : accountOpen
      ? 'Account'
      : selectedVehicle
        ? `${selectedVehicle.year} ${selectedVehicle.make} ${selectedVehicle.model}, vehicle detail`
        : !inventoryOpen
          ? 'The Yard, home'
          : loadState === 'loading'
            ? 'Loading inventory'
            : loadState === 'error'
              ? 'The inventory API could not be reached'
              : 'Vehicle inventory';
  // #endregion announcement

  return {
    announcement,
    backTo,
    goHome,
    openInventory,
    openVehicle,
    openVehicleById,
    backToInventory,
    openAdmin,
    openAdminCard,
    closeAdmin,
    openAccount,
    closeAccount,
    openDocument,
  };
}
