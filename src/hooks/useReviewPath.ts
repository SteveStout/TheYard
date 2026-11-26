/**
 * Does:      Keeps the review route's short path: the stops reached on this browser, under the path's own key, and the
 *            two live reads its tiles draw, the chart's readings and the live lot, each read once in the background; the
 *            lot starts from the one the server drew the page with.
 * Does not:  Report anything (the page's action does) or pick what a tile prints (reviewPath.ts does).
 * Used by:   ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import { useFirstLoad, useInTheBrowser } from './useFirstLoad';
import { request } from '../lib/request';
import type { StorageLike } from '../lib/reviewProgress';
import { LOT_URL, lotFrom, markPath, readPath, type PathLot } from '../lib/reviewPath';

/** The browser's own storage, or null where there is none or it refuses to be asked for. */
function browserStorage(): StorageLike | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

/** An answer read in the background: the JSON on a 2xx, null on anything else, never a throw. */
function read(url: string): Promise<unknown> {
  return request(url, undefined, { background: true })
    .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
    .catch(() => null);
}

/** The last lot this page read, so the route opened again in the same visit starts from it rather than from nothing. */
let lastLot: PathLot | null = null;

export function useReviewPath() {
  const inBrowser = useInTheBrowser();
  const first = useFirstLoad();
  const [moved, setMoved] = useState<string[] | null>(null);
  // Drawn on the server and taken over with nothing reached, so the two draws agree; the kept place then shows.
  const reached = moved ?? (inBrowser ? readPath(browserStorage()) : []);
  const [readings, setReadings] = useState<string[] | null>(null);
  // The lot the server drew the page with (or the last one read in this visit), so the tile and its button are never
  // empty; a later read replaces it only with a lot, so one in flight or one that fails keeps the last.
  const [lot, setLot] = useState<PathLot | null>(() => lotFrom(first?.lot ?? null) ?? lastLot);
  useEffect(() => {
    let live = true;
    void read('/api/review/trend').then((answer) => {
      const lines = (answer as { readings?: unknown } | null)?.readings;
      if (live && Array.isArray(lines))
        setReadings(lines.filter((line) => typeof line === 'string'));
    });
    void read(LOT_URL).then((answer) => {
      const next = lotFrom(answer);
      if (next) lastLot = next;
      if (live && next) setLot(next);
    });
    return () => {
      live = false;
    };
  }, []);
  return {
    reached,
    readings,
    lot,
    /** A stop's button was pressed: it counts as reached on this browser. */
    reach: (key: string) => setMoved(markPath(browserStorage(), reached, key)),
  };
}
