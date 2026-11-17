/**
 * Does:      Brings the card an address names into view once the card has drawn: the review route's stops 4 and 5
 *            open the Admin tab at a card, and the card starts under the tiles (ADR: Ten minutes, seven stops). Only
 *            the card the page opened on, once: a card opened from the rail afterwards stays where the reader is.
 * Does not:  Read the card's data or choose the card: the address does that, and AdminWorkbench draws it.
 * Used by:   AdminWorkbench.tsx.
 */
import { useEffect, useRef } from 'react';
import type { CardSlug } from '../../../lib/workbench';

/** The ref for the open card's column; its heading is scrolled to the top when the address named <open>. */
export function useAddressedCardIntoView(open: CardSlug) {
  const column = useRef<HTMLDivElement>(null);
  // The card the address named when the page opened, and whether it has been brought into view yet.
  // Trimmed as the address handler trims it before it picks the card (cardFromAddress).
  const addressed = useRef(
    typeof window === 'undefined'
      ? null
      : (new URLSearchParams(window.location.search).get('card')?.trim() ?? null)
  );
  const broughtIntoView = useRef(false);

  useEffect(() => {
    const here = column.current;
    if (!here || broughtIntoView.current || addressed.current !== open) return;
    const bring = () => {
      const heading = here.querySelector('h2');
      if (!heading) return false;
      heading.scrollIntoView({ block: 'start' });
      broughtIntoView.current = true;
      return true;
    };
    if (bring()) return;
    // A lazy card draws its heading when its chunk arrives, however long that takes; the watch ends with it, or
    // when the open card changes.
    const watch = new MutationObserver(() => {
      if (bring()) watch.disconnect();
    });
    watch.observe(here, { childList: true, subtree: true });
    return () => watch.disconnect();
  }, [open]);

  return column;
}
