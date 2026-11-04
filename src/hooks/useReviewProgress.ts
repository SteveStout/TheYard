import { useEffect, useState } from 'react';
import { useInTheBrowser } from './useFirstLoad';
import { linkCode } from '../lib/links';
import { request } from '../lib/request';
import {
  emptyProgress,
  markReached,
  moveTo,
  newlyReached,
  reachBody,
  readProgress,
  writeProgress,
  type ReviewProgress,
  type StorageLike,
} from '../lib/reviewProgress';
import { REVIEW_STOPS } from '../lib/reviewStops';

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
function report(stop: number, code: string | null): void {
  void request(
    '/api/review/reached',
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(reachBody(stop, code)),
      keepalive: true,
    },
    { background: true }
  ).catch(() => {});
}

/**
 * The reviewer's place on the route (ADR: Ten minutes, seven stops), kept on this browser. The
 * page is drawn on the server at the first stop, and hydrated at the first stop too, so the two
 * agree; once the page is in the browser the kept place is what shows, and every move is kept.
 */
export function useReviewProgress() {
  const inBrowser = useInTheBrowser();
  // A move made on this page; until there is one, the place is the kept one.
  const [moved, setMoved] = useState<{ progress: ReviewProgress; finished: boolean } | null>(null);
  const kept = inBrowser ? readProgress(browserStorage()) : emptyProgress();
  const progress = moved?.progress ?? kept;
  const finished = moved?.finished ?? kept.reached.length === REVIEW_STOPS.length;
  // The code the page opened on, read during the first render, before the address bar is rewritten.
  const [code] = useState(() =>
    typeof window === 'undefined' ? null : linkCode(window.location.search)
  );
  // The route opened: stop 0, once for this page.
  useEffect(() => report(0, code), [code]);

  // Every move but the last Mark done puts a stop back on screen, so the route is not finished. A stop
  // reached for the first time on this browser is reported once; the reached set is what is kept.
  const keep = (next: ReviewProgress, done = false) => {
    for (const stop of newlyReached(progress, next)) report(stop, code);
    setMoved({ progress: next, finished: done });
    writeProgress(browserStorage(), next);
  };
  const last = REVIEW_STOPS.length - 1;
  // Done with the stop on screen: it is reached, and the one after it is current.
  const doneWithCurrent = (finishing = false) =>
    keep(moveTo(markReached(progress, progress.current), progress.current + 1), finishing);

  return {
    progress,
    /** Every stop is done: the finish panel shows. */
    finished,
    /** The stop before, made current; what was reached stays reached. */
    previous: () => keep(moveTo(progress, progress.current - 1)),
    /** On to the next stop: the one on screen counts as reached. */
    next: () => doneWithCurrent(),
    /** A stop's head was pressed: that stop is current. */
    choose: (index: number) => keep(moveTo(progress, index)),
    /** The stop was opened: it counts as reached. */
    opened: (index: number) => keep(markReached(progress, index), finished),
    /** The stop on screen is done, as Next stop; on the last stop the route is finished. */
    markDone: () => doneWithCurrent(progress.current === last),
    /** Back to the first stop, with what was reached kept. */
    restart: () => keep(moveTo(progress, 0)),
  };
}
