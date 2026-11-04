/**
 * Does:      Opens the review route at ?view=review as a history entry, so Back returns to the view it was opened from.
 * Does not:  Close the other views' state of its own accord beyond what the caller hands it, or write the address bar itself.
 * Used by:   useNavigation.ts.
 */
import type { Address } from './useAddressBar';

/** The review route's one move (ADR: Ten minutes, seven stops), the same shape as the blog's. */
export function useReviewNavigation(address: Address, closeOthers: () => void) {
  /** The route's own page; pushed, so Back returns home when the landing page's banner or the rail opened it. */
  const openReview = () => {
    address.push({ viaReview: true }, new URLSearchParams({ view: 'review' }));
    closeOthers();
    address.setReviewOpen(true);
  };

  return { openReview };
}
