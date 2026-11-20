/**
 * Does:      Says which of the site map's actions the sidebar's foot draws on the open view: every one of them, but on
 *            the review route, where a stranger reads the page, Admin is not drawn, and Sign in is not drawn while
 *            nobody is signed in (a signed-in visitor still sees their own account row).
 * Does not:  Draw a row (RailPinnedRows.tsx does) or know the order (the site map's actions are in it already).
 * Used by:   RailPinnedRows.tsx, railRows.test.ts.
 */
import type { SiteAction } from './siteMapTypes';

/** The rows the review route leaves out of the rail and the phone's drawer. */
export const REVIEW_HIDES = ['account', 'admin'] as const;

/** The actions drawn in the rail on this view, in the site map's order. */
export function railActions(
  actions: readonly SiteAction[],
  view: { reviewOpen: boolean; signedIn: boolean }
): SiteAction[] {
  return actions.filter(
    (action) =>
      action.inRail &&
      !(view.reviewOpen && (action.key === 'admin' || (action.key === 'account' && !view.signedIn)))
  );
}
