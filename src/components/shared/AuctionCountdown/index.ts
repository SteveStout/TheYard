/**
 * Does:      The folder entry for AuctionCountdown: re-exports AuctionCountdown, so an import names the folder,
 *            components/shared/AuctionCountdown.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   BidPanel.tsx, VehicleCard.tsx, VehicleDetail.tsx.
 */
export * from './AuctionCountdown';
