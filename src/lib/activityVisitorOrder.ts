// The visitor table's order: which columns it sorts on and the sort itself.
// Its own file because the table's order is its own decision, apart from how
// the rows are counted or drawn.

import type { ActivityVisitor } from './activityTypes';

// #region visitor-order
/** The columns the visitor table can be sorted on. */
export type VisitorSortKey = 'last_seen' | 'first_seen' | 'requests' | 'network' | 'store';

/** The table's order: newest first by default, and any column either way on a click. */
export function sortVisitors(
  rows: ActivityVisitor[],
  key: VisitorSortKey,
  descending: boolean
): ActivityVisitor[] {
  const sorted = [...rows].sort((a, b) => {
    const left = a[key];
    const right = b[key];
    if (typeof left === 'number' && typeof right === 'number') return left - right;
    return String(left).localeCompare(String(right));
  });
  return descending ? sorted.reverse() : sorted;
}
// #endregion visitor-order
