/**
 * Does:      The folder entry for GatePopover: re-exports GatePopover, so an import names the folder,
 *            components/layout/GatePopover.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   StoreBar.tsx, by a lazy import.
 */
export * from './GatePopover';
