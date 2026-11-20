import { useEffect, useState } from 'react';
import { useInTheBrowser } from './useFirstLoad';
import { linkCode } from '../lib/links';
import { request } from '../lib/request';
import {
  actionBody,
  emptyProgress,
  markReached,
  newlyReached,
  reachBody,
  readProgress,
  writeProgress,
  type ReviewAction,
  type ReviewProgress,
  type StorageLike,
} from '../lib/reviewProgress';

/** The browser's own storage, or null where there is none or it refuses to be asked for. */
function browserStorage(): StorageLike | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

/**
 * Tells the API a stop was reached (ADR: Ten minutes, seven stops, the addendum on the reach rows), in the
 * background: a send that fails is let go and never blocks the page, and keepalive lets one sent as a stop's
 * link is followed finish after the page has gone.
 */
function report(stop: number, code: string | null, screen: string): void {
  send(reachBody(stop, code, screen));
}

/** One report to the API, in the background; keepalive lets one sent as a link is followed finish after the page has gone. */
function send(body: object): void {
  void request(
    '/api/review/reached',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      keepalive: true,
    },
    { background: true }
  ).catch(() => {});
}

/**
 * The reviewer's place on the route (ADR: Ten minutes, seven stops), kept on this browser: the stops reached. The page
 * is drawn on the server with nothing reached, and hydrated so too, so the two agree; once the page is in the browser
 * the kept place is what shows, and every stop opened is kept.
 */
export function useReviewProgress(screen: string = 'verdict') {
  const inBrowser = useInTheBrowser();
  // A move made on this page; until there is one, the place is the kept one.
  const [moved, setMoved] = useState<ReviewProgress | null>(null);
  const progress = moved ?? (inBrowser ? readProgress(browserStorage()) : emptyProgress());
  // The code the page opened on, read during the first render, before the address bar is rewritten.
  const [code] = useState(() =>
    typeof window === 'undefined' ? null : linkCode(window.location.search)
  );
  // The route opened: stop 0, once for this page.
  useEffect(() => report(0, code, screen), [code, screen]);

  // A stop reached for the first time on this browser is reported once; the reached set is what is kept.
  const keep = (next: ReviewProgress) => {
    for (const stop of newlyReached(progress, next)) report(stop, code, screen);
    setMoved(next);
    writeProgress(browserStorage(), next);
  };

  return {
    /** The stops reached on this browser, each an index from 0. */
    reached: progress.reached,
    /** A tour cell was opened: its stop counts as reached. */
    opened: (index: number) => keep(markReached(progress, index)),
    /** Start over: the place kept on this browser is cleared, so no stop reads as seen. */
    startOver: () => keep(emptyProgress()),
    /**
     * A button on the route was pressed (ADR: Ten minutes, seven stops, the addendum on the second actions):
     * reported by its name with the code the page opened on. The API keeps each name once a visitor a day,
     * so a second press costs a request and changes nothing.
     */
    action: (name: ReviewAction) => send(actionBody(name, code, screen)),
    /** The code the page opened on, or null: the send line shares it, so a second device counts under it. */
    code,
  };
}
