/**
 * Does:      Keeps the vehicle on screen current: its price when another bidder moves it, a bid placed on it, buying it now.
 * Does not:  Open or close the vehicle's view (useNavigation.ts does), or hold the bids themselves (useBids does, inside useInventory.ts).
 * Used by:   App.tsx.
 */
import { useEffect, useMemo } from 'react';
import { fetchVehicleById } from '../../lib/data';
import { applyBidRecord } from '../../hooks/useBids';
import type { Address } from './useAddressBar';
import type { Inventory } from './useInventory';

export function useOpenVehicle(address: Address, inventory: Inventory) {
  const { selectedVehicle, setSelectedVehicle, selectedIdRef } = address;
  const { bids, placeBid, buyNow } = inventory;

  // Placing a bid does not mean you are leading: another bidder may have
  // answered (ADR-027). The server decides; the page only reads its answer.
  // #region refresh-open-vehicle
  // The page can overlay current_bid, but not min_next_bid: the bid increment
  // rules live only on the server. So when another bidder moves the price on
  // the open vehicle, it is refetched rather than patched. Otherwise the panel
  // could show a minimum bid that the server would reject.
  const openMarketAmount = selectedVehicle
    ? (bids[selectedVehicle.id]?.market_amount ?? null)
    : null;
  useEffect(() => {
    const id = selectedIdRef.current;
    if (!id || openMarketAmount === null) return;
    let cancelled = false;
    void fetchVehicleById(id)
      .then((fresh) => {
        if (!cancelled && fresh && selectedIdRef.current === fresh.id) setSelectedVehicle(fresh);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [openMarketAmount, selectedIdRef, setSelectedVehicle]);
  // #endregion refresh-open-vehicle

  /** The snapshot with the buyer's live bid state layered on. */
  const selected = useMemo(
    () => (selectedVehicle ? applyBidRecord(selectedVehicle, bids[selectedVehicle.id]) : undefined),
    [selectedVehicle, bids]
  );

  const onPlaceBid = async (amount: number) => {
    if (!selected) return { kind: 'rejected' as const, reason: 'No vehicle selected.' };
    const result = await placeBid(selected, amount);
    if (result.vehicle) setSelectedVehicle(result.vehicle);
    return result.outcome;
  };

  const onBuyNow = async () => {
    if (!selected) return { kind: 'rejected' as const, reason: 'No vehicle selected.' };
    const result = await buyNow(selected);
    if (result.vehicle) setSelectedVehicle(result.vehicle);
    return result.outcome;
  };

  return {
    /** The vehicle on screen with this visitor's bid on it, or undefined when none is open. */
    selected,
    isHighBidder: selected ? inventory.highBidderIds.has(selected.id) : false,
    isOutbid: selected ? inventory.outbidIds.has(selected.id) : false,
    wonBuyNow: selected ? inventory.wonIds.has(selected.id) : false,
    onPlaceBid,
    onBuyNow,
  };
}
