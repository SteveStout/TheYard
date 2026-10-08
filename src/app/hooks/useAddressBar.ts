/**
 * Does:      Holds the open view (the page, the vehicle, the Admin card, the document, the filters and sort)
 *            and keeps the address bar in step with it, both ways: the first-load reads, deep links, Back and Forward.
 * Does not:  Decide when a view opens (useNavigation.ts does), fetch the vehicle list, or draw anything.
 * Used by:   App.tsx, useNavigation.ts, useInventory.ts, useOpenVehicle.ts.
 */
import { useEffect, useRef, useState } from 'react';
import { fetchVehicleById } from '../../lib/data';
import type { Vehicle } from '../../lib/types';
import {
  EMPTY_FILTERS,
  filtersFromSearchParams,
  filtersToSearchParams,
  opensInventory,
  type InventoryFilters,
  type SortKey,
} from '../../lib/inventory';
import { cardFromAddress, pinFromAddress, type CardSlug } from '../../lib/workbench';
import { docKeyForSlug, docSlug } from '../../library/addresses';
import type { DocKey } from '../../library/documents';
import { useFirstLoad } from '../../hooks/useFirstLoad';
import { browserSearch, readFirstAddress } from '../firstAddress';

// This file is the only code that writes the address bar. Everything else
// that needs to push or replace an entry asks through the four functions it
// hands back (push, replace, replaceQuery, stepBack), so a reader looking for
// "what put that in the URL" has one file to read.

/** Where a view's back button goes. Its label says which: "Back to home" or inventory. */
export type BackTo = 'home' | 'inventory';

/**
 * What a history entry this site pushed remembers: which kind of view opened
 * it (so that view's close button can go back rather than forward), where its
 * back button leads, and how many Admin cards deep it is.
 */
export type HistoryEntry = {
  viaTile?: boolean;
  viaInventory?: boolean;
  viaAdmin?: boolean;
  viaAccount?: boolean;
  viaDoc?: boolean;
  viaHome?: boolean;
  from?: BackTo;
  benchDepth?: number;
};

export type Address = ReturnType<typeof useAddressBar>;

export function useAddressBar() {
  // The address the page opened on, read once: the one the rendering service drew, or the address bar.
  const firstLoad = useFirstLoad();
  const [first] = useState(() => readFirstAddress(firstLoad?.search ?? browserSearch()));
  const [filters, setFilters] = useState<InventoryFilters>(first.filters);
  const [sort, setSort] = useState<SortKey>(first.sort);
  /** Snapshot of the opened vehicle, so the detail view survives page refetches. */
  const [selectedVehicle, setSelectedVehicle] = useState<Vehicle | null>(
    firstLoad?.vehicle ?? null
  );
  /** The Admin tab (ADR-010): health, errors, and Azure's view, ?view=admin. */
  const [adminOpen, setAdminOpen] = useState(first.admin);
  // #region admin-card
  // The Admin workbench's open card and its pinned card (ADR-080). They live
  // here, beside the view, because the url-mirror effect below is the only code
  // that writes the address bar. If the address names a card that does not
  // exist, cardAsked keeps the name so the rail can say so, and Health opens.
  const [adminCard, setAdminCard] = useState<CardSlug>(first.card.slug);
  const [adminPin, setAdminPin] = useState<CardSlug | null>(first.pin);
  const [cardAsked, setCardAsked] = useState<string | null>(
    first.card.known ? null : first.card.asked
  );
  // #endregion admin-card
  /** The account view (ADR-037), ?view=account. */
  const [accountOpen, setAccountOpen] = useState(first.account);
  /** True when the base view is the inventory list rather than the landing page. */
  const [inventoryOpen, setInventoryOpen] = useState(first.inventory);
  const [openDocKey, setOpenDocKey] = useState<DocKey | null>(first.docKey);

  // #region url-mirror
  // Mirror the current view into the address bar: the filter GET parameters
  // plus ?vehicle={id} when a detail page is open. replaceState here, because
  // typing shouldn't pile up history entries; opening a tile pushes its own
  // entry (openVehicle, in useNavigation.ts) so the browser's Back button
  // closes the detail view.
  // A vehicle the rendering service already read is no longer pending: it is on the page.
  const deepLinkPending = useRef(first.vehicleId !== null && !firstLoad?.vehicle);
  useEffect(() => {
    if (deepLinkPending.current) return;
    // The landing page carries no filters: they belong to the inventory, and an
    // address with one in it opens the inventory (opensInventory).
    const onLanding = !inventoryOpen && !selectedVehicle && !adminOpen && !accountOpen;
    const params = onLanding ? new URLSearchParams() : filtersToSearchParams(filters, sort);
    if (selectedVehicle) params.set('vehicle', selectedVehicle.id);
    const listOnly = inventoryOpen && !selectedVehicle && !adminOpen && !accountOpen;
    if (listOnly && !opensInventory(params)) params.set('view', 'inventory');
    if (adminOpen) {
      params.set('view', 'admin');
      // Health is the card ?view=admin opens, so it is the one card the address leaves unnamed.
      if (adminCard !== 'health') params.set('card', adminCard);
      if (adminPin !== null) params.set('pin', adminPin);
    }
    if (accountOpen) params.set('view', 'account');
    if (openDocKey) params.set('doc', docSlug(openDocKey));
    const query = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      query ? `?${query}` : window.location.pathname
    );
  }, [
    filters,
    sort,
    selectedVehicle,
    adminOpen,
    adminCard,
    adminPin,
    accountOpen,
    openDocKey,
    inventoryOpen,
  ]);
  // #endregion url-mirror

  // Restore a deep-linked detail view on first load (?vehicle={id}).
  useEffect(() => {
    if (!first.vehicleId || !deepLinkPending.current) return;
    let live = true;
    fetchVehicleById(first.vehicleId)
      .then((vehicle) => {
        if (!live) return;
        deepLinkPending.current = false;
        if (vehicle) {
          setSelectedVehicle(vehicle);
        } else {
          // Unknown id: drop the dead parameter and stay on the list.
          const params = new URLSearchParams(window.location.search);
          params.delete('vehicle');
          const query = params.toString();
          window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
        }
      })
      .catch(() => {
        deepLinkPending.current = false;
      });
    return () => {
      live = false;
    };
  }, [first]);

  // #region back-forward
  // Browser Back/Forward: re-read the whole view from the URL. The listener is
  // registered once, so it reads the open vehicle through selectedIdRef.
  const selectedIdRef = useRef<string | null>(null);
  useEffect(() => {
    selectedIdRef.current = selectedVehicle?.id ?? null;
  }, [selectedVehicle]);
  useEffect(() => {
    const onPopState = () => {
      const params = new URLSearchParams(window.location.search);
      const restored = filtersFromSearchParams(params);
      setFilters(restored.filters);
      setSort(restored.sort);
      setAdminOpen(params.get('view') === 'admin');
      const card = cardFromAddress(params.get('card'));
      setAdminCard(card.slug);
      setAdminPin(pinFromAddress(params.get('pin')));
      setCardAsked(card.known ? null : card.asked);
      setAccountOpen(params.get('view') === 'account');
      setInventoryOpen(opensInventory(params));
      setOpenDocKey(docKeyForSlug(params.get('doc')));
      const vehicleId = params.get('vehicle');
      if (!vehicleId) {
        setSelectedVehicle(null);
        return;
      }
      if (vehicleId === selectedIdRef.current) return;
      fetchVehicleById(vehicleId)
        .then((vehicle) => setSelectedVehicle(vehicle))
        .catch(() => setSelectedVehicle(null));
    };
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);
  // #endregion back-forward

  // Where the reader is looking, worked out once for everything that asks:
  // nothing covers the page when no vehicle, Admin, Account or document is open.
  const nothingCovers = !adminOpen && !accountOpen && !selectedVehicle && !openDocKey;
  /** The landing page is what shows. */
  const homeShowing = !inventoryOpen && nothingCovers;
  /** The inventory list is what shows. */
  const listShowing = inventoryOpen && nothingCovers;

  // The four ways the rest of the app changes history, each one line of the
  // browser's own API, kept here so this file stays the only writer.
  /** A new entry, so Back returns to the view before it. No query means the bare landing address. */
  const push = (entry: HistoryEntry, params: URLSearchParams | null) =>
    window.history.pushState(entry, '', params ? `?${params}` : window.location.pathname);
  /** The same entry, holding a new address and a new memory. */
  const replace = (entry: HistoryEntry, params: URLSearchParams) =>
    window.history.replaceState(entry, '', `?${params}`);
  /** The same entry, holding only an address: a view closed in place, with nothing behind it to go back to. */
  const replaceQuery = (params: URLSearchParams) => {
    const query = params.toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  };
  /** Back one entry, or several at once: Admin leaves past every card it opened. */
  const stepBack = (entries = 1) =>
    entries === 1 ? window.history.back() : window.history.go(-entries);
  /** What the entry on screen remembers, or nothing when the site did not push it or there is no window. */
  const entry = () =>
    typeof window === 'undefined' ? null : (window.history.state as HistoryEntry | null);

  return {
    filters,
    /** One filter or several changed; the rest stay. */
    patchFilters: (patch: Partial<InventoryFilters>) =>
      setFilters((prev) => ({ ...prev, ...patch })),
    clearFilters: () => setFilters(EMPTY_FILTERS),
    sort,
    setSort,
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
    homeShowing,
    listShowing,
    /** True while a ?vehicle= link from the first load is still being looked up. */
    deepLinkPending,
    /** The open vehicle's id, readable from a listener registered once. */
    selectedIdRef,
    /** The first load named a vehicle (?vehicle=), so the list is needed even before it shows. */
    startedOnVehicleLink: first.vehicleId !== null,
    /** A password reset link's token, from the first load. */
    resetToken: first.resetToken,
    push,
    replace,
    replaceQuery,
    stepBack,
    entry,
  };
}
