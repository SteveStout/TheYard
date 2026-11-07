/**
 * Does:      Names the view on screen twice over: a key that changes whenever the view does, so focus can follow it, and
 *            the words a screen reader hears when it arrives.
 * Does not:  Move focus or announce anything itself (useNavigation.ts does both), or read the address bar.
 * Used by:   useNavigation.ts.
 */
import type { Vehicle } from '../lib/types';

/** Which view shows, as useAddressBar holds it, and how the list is loading. */
export type ViewOnScreen = {
  adminOpen: boolean;
  blogOpen: boolean;
  blogPost: string | null;
  reviewOpen: boolean;
  recommendationsOpen: boolean;
  accountOpen: boolean;
  inventoryOpen: boolean;
  selectedVehicle: Vehicle | null;
  loadState: 'loading' | 'ready' | 'error';
};

/** A key for the view on screen, one per view and per vehicle and post, so a change of view is a change of key. */
export function viewKeyOf(view: ViewOnScreen): string {
  if (view.adminOpen) return 'admin';
  if (view.blogOpen) return `blog:${view.blogPost ?? ''}`;
  if (view.reviewOpen) return 'review';
  if (view.recommendationsOpen) return 'recommendations';
  if (view.accountOpen) return 'account';
  if (view.selectedVehicle) return `vehicle:${view.selectedVehicle.id}`;
  return 'list';
}

/**
 * Focus says where you are; this says what you arrived at, since a focused container is announced
 * unevenly. The match count is left to the filter bar's own status line: one live region says it once.
 */
export function announcementOf(view: ViewOnScreen): string {
  if (view.adminOpen) return 'Admin panel';
  if (view.blogOpen) return 'Blog';
  if (view.reviewOpen) return 'Check my work in ten minutes';
  if (view.recommendationsOpen) return 'The people who worked with Steven';
  if (view.accountOpen) return 'Account';
  const vehicle = view.selectedVehicle;
  if (vehicle) return `${vehicle.year} ${vehicle.make} ${vehicle.model}, vehicle detail`;
  if (!view.inventoryOpen) return 'The Yard, home';
  if (view.loadState === 'loading') return 'Loading inventory';
  if (view.loadState === 'error') return 'The inventory API could not be reached';
  return 'Vehicle inventory';
}
