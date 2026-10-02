/**
 * The two reads behind the activity card: the public report for the chosen window, and
 * the per-visitor rows, which are asked for only with the operator's key on a site that
 * serves them. It is its own file so the card reads as layout and this file as fetching.
 */
import { useEffect, useState } from 'react';
import type { ActivityReport, ActivityVisitors, ActivityWindow } from '../../../lib/activity';
import type { Fetched } from '../shared/types';

/** What the card needs from its reads: the window, a way to change it, and both answers. */
export type ActivityReports = {
  /** The window the card is showing. */
  window: ActivityWindow;
  /** Switches to another window and puts both reads back to loading. */
  chooseWindow: (next: ActivityWindow) => void;
  /** The public report: null while loading, 'failed' when the read did not answer. */
  report: Fetched<ActivityReport>;
  /** The per-visitor rows: null while loading or not asked for, 'failed' when refused. */
  visitors: Fetched<ActivityVisitors>;
};

/**
 * Reads the activity report, and the visitor rows when the key and the site allow them,
 * again on every change of window. The visitor read sends the key in the X-Admin-Key
 * header, and the endpoint answers 404 to anybody without it.
 */
export function useActivityReports(
  adminKey: string | null,
  rowsServed: boolean | null,
  onReport: (report: ActivityReport) => void
): ActivityReports {
  const [window_, setWindow] = useState<ActivityWindow>('7d');
  const [report, setReport] = useState<Fetched<ActivityReport>>(null);
  const [visitors, setVisitors] = useState<Fetched<ActivityVisitors>>(null);

  useEffect(() => {
    let live = true;
    void fetch(`/api/admin/activity?window=${window_}`)
      .then((r) =>
        r.ok ? (r.json() as Promise<ActivityReport>) : Promise.reject(new Error(String(r.status)))
      )
      .then((v) => {
        if (live) {
          setReport(v);
          onReport(v);
        }
      })
      .catch(() => {
        if (live) setReport('failed');
      });
    if (adminKey !== null && rowsServed === true) {
      void fetch(`/api/admin/activity/visitors?window=${window_}`, {
        headers: { 'X-Admin-Key': adminKey },
      })
        .then((r) =>
          r.ok
            ? (r.json() as Promise<ActivityVisitors>)
            : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) setVisitors(v);
        })
        .catch(() => {
          if (live) setVisitors('failed');
        });
    }
    return () => {
      live = false;
    };
  }, [window_, adminKey, rowsServed, onReport]);

  const chooseWindow = (next: ActivityWindow) => {
    // The change of window is the event, so the reads go back to loading here rather
    // than inside the effect that fetches. The window already showing is not a change:
    // the effect would not run again and the card would stay on "Loading" for good.
    if (next === window_) return;
    setWindow(next);
    setReport(null);
    setVisitors(null);
  };

  return { window: window_, chooseWindow, report, visitors };
}
