// The click rule for every link that opens an Admin card: the rail's links and the strip's
// tiles. A plain click opens the card in place; anything that asks for a new tab is left to
// the browser. It is its own file because the rail and the strip both follow the same rule.
import type { MouseEvent } from 'react';

/**
 * Click handler for card links. A plain left click opens the card in place.
 * A click with a modifier key, or the middle button, means "open in a new tab",
 * so we leave it to the browser, which follows the link's href.
 */
export function followCardLink(event: MouseEvent<HTMLElement>, open: () => void) {
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return;
  }
  event.preventDefault();
  open();
}
