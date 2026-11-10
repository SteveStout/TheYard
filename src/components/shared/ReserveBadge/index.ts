/**
 * Does:      The folder entry for ReserveBadge: re-exports ReserveBadge, so an import names the folder,
 *            components/shared/ReserveBadge.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   BidPanel.tsx, VehicleCard.tsx, VehicleDetail.tsx.
 */
export * from './ReserveBadge';
