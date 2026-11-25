/**
 * Does:      Names what a history entry this site pushed remembers, and where a view's back button goes.
 * Does not:  Write the address bar (useAddressBar.ts is the only code that does) or read one.
 * Used by:   useAddressBar.ts.
 */

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
  viaBlog?: boolean;
  viaReview?: boolean;
  viaHome?: boolean;
  from?: BackTo;
  benchDepth?: number;
};
