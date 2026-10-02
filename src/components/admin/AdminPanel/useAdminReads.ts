// The reads the Admin tab's strip makes for itself: a refresh tick, the site's health, its
// recent errors, the last page check and the activity report behind today's visitors. The
// open card is handed the same values, so a tile never disagrees with the card it links to.
// It is its own file because it is the tab's data and timers, apart from how the tab is drawn.
import { useCallback, useEffect, useState } from 'react';
import type { ActivityReport } from '../../../lib/activity';
import { visitorsOn } from '../../../lib/statTiles';
import type { ErrorEntry, Fetched, Health, PageStatus } from '../shared/types';
import { REFRESH_MS } from '../shared/common';

/** The page check's two numbers: how many pages were checked and how many answered. */
export type PagesSeen = { checked: number; up: number };

/** What useAdminReads gives back. */
export type AdminReads = {
  /** Goes up by one every REFRESH_MS while the browser tab is visible; reads re-run when it moves. */
  tick: number;
  /** The site's health answer: null while loading, 'failed' when the read failed. */
  health: Fetched<Health>;
  /** The recent server errors, in the same three states. */
  errors: Fetched<ErrorEntry[]>;
  /** The last page check, from the strip's own read or from the Pages card when it is open. */
  pagesSeen: PagesSeen | null;
  /** Records a page check the Pages card read, so the tile moves with the card. */
  setPagesSeen: (seen: PagesSeen) => void;
  /** People who visited today, from the activity report; null until it answers. */
  visitorsToday: number | null;
  /** Whether this site serves per-visitor rows (ADR-071); null until the activity report answers. */
  rowsServed: boolean | null;
  /** Takes an activity report, from the strip's read or the Activity card's, and keeps its two facts. */
  onActivity: (report: ActivityReport) => void;
};

/**
 * Reads what the strip needs when the tab opens and again on every tick. Cards
 * read their own data, and only while open. Each read stops counting once the
 * tab is gone, so a late answer never sets stale state, and a network error or
 * a non-200 answer becomes 'failed' rather than endless loading.
 */
export function useAdminReads(): AdminReads {
  // Whether this site serves per-visitor rows (ADR-071). The activity report
  // tells us; null until it answers.
  const [rowsServed, setRowsServed] = useState<boolean | null>(null);
  // Two numbers the tiles need that other reads supply: today's visitors
  // (from the activity report) and the last check of every page.
  const [visitorsToday, setVisitorsToday] = useState<number | null>(null);
  const [pagesSeen, setPagesSeen] = useState<PagesSeen | null>(null);
  const onActivity = useCallback((report: ActivityReport) => {
    setRowsServed(report.visitor_rows);
    setVisitorsToday(visitorsOn(report.days, new Date()));
  }, []);
  const [health, setHealth] = useState<Fetched<Health>>(null);
  const [errors, setErrors] = useState<Fetched<ErrorEntry[]>>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    // Bump `tick` every REFRESH_MS. The strip and the open card re-read when
    // it changes. Skip it while the browser tab is hidden: nobody is looking.
    const id = window.setInterval(() => {
      if (!document.hidden) setTick((t) => t + 1);
    }, REFRESH_MS);
    return () => window.clearInterval(id);
  }, []);

  useEffect(() => {
    let live = true;
    // The strip's own reads: health, recent errors and the page check.
    // Cards read their own data, and only while open.
    // `live` goes false on cleanup, so a late answer never sets stale state.
    // A network error or non-200 answer becomes 'failed', not endless loading.
    const grab = <T>(url: string, set: (v: Fetched<T>) => void) =>
      fetch(url)
        .then((r) =>
          r.ok ? (r.json() as Promise<T>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) set(v);
        })
        .catch(() => {
          if (live) set('failed');
        });
    void grab<Health>('/api/health', setHealth);
    void grab<ErrorEntry[]>('/api/errors', setErrors);
    // The strip reads the page check itself, because the Pages card that also
    // reads it is only mounted while it is open.
    void grab<PageStatus>('/api/admin/pages', (answer) => {
      if (answer !== null && answer !== 'failed' && answer.report !== null) {
        setPagesSeen({ checked: answer.report.checked, up: answer.report.up });
      }
    });
    return () => {
      live = false;
    };
  }, [tick]);

  // Read the activity report when the tab opens and again on every tick, for
  // today's visitors and for `rowsServed`, so the visitors tile moves with the
  // rest of the strip instead of keeping the count it had when the tab opened.
  // The Activity card, when open, does its own read and passes the result
  // back through onActivity.
  useEffect(() => {
    let live = true;
    void fetch('/api/admin/activity?window=7d')
      .then((r) =>
        r.ok ? (r.json() as Promise<ActivityReport>) : Promise.reject(new Error(String(r.status)))
      )
      .then((report) => {
        if (live) onActivity(report);
      })
      .catch(() => {});
    return () => {
      live = false;
    };
  }, [onActivity, tick]);

  return {
    tick,
    health,
    errors,
    pagesSeen,
    setPagesSeen,
    visitorsToday,
    rowsServed,
    onActivity,
  };
}
