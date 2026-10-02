// The machine stats the Admin tab shares: one read of the container's numbers, and the row of
// window buttons (1h, 24h, 7d, 30d) every chart on the tab follows. The tiles, the Traffic card
// and the Machines card all take it from here, so they always cover the same stretch of time.
// It is its own file because it is a read with its own timer, apart from how the tab is drawn.
import { type ReactNode, useEffect, useState } from 'react';
import { MACHINE_WINDOWS, type MachineWindow, windowName } from '../../../lib/machineChart';
import cardStyles from '../shared/card.module.css';
import type { Fetched, Machines } from '../shared/types';

/** What useMachines gives back: the read, the last good answer, the window and its buttons. */
export type MachinesRead = {
  /** The answer for the window shown now: null while loading, 'failed' when the read failed. */
  machines: Fetched<Machines>;
  /** The last good answer, kept while a new window loads so the tiles do not flash "waiting". */
  latest: Machines | null;
  /** The stretch of time every chart on the tab covers. */
  window: MachineWindow;
  /** Draws one row of window buttons; `where` names the row for screen readers. */
  toolbar: (where: string, testPrefix: string) => ReactNode;
};

// #region machines-read
/**
 * Reads machine stats once and shares them with the Traffic card, the Machines
 * card and the tiles. All three use one time window, so they always cover the
 * same stretch and can be compared. It is a hook, not a component, because the
 * two cards live in different places on the page.
 */
export function useMachines(): MachinesRead {
  const [machines, setMachines] = useState<Fetched<Machines>>(null);
  // The last good answer. Unlike `machines`, it is not cleared when the window
  // changes, so the tiles keep their values instead of flashing "waiting".
  const [latest, setLatest] = useState<Machines | null>(null);
  const [window_, setWindow] = useState<MachineWindow>('1h');

  useEffect(() => {
    let live = true;
    const read = () =>
      fetch(window_ === '1h' ? '/api/admin/machines' : `/api/admin/machines?window=${window_}`)
        .then((r) =>
          r.ok ? (r.json() as Promise<Machines>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (!live) return;
          setMachines(v);
          setLatest(v);
        })
        .catch(() => {
          if (live) setMachines('failed');
        });
    void read();
    // The server samples every 15 seconds. We re-read every 30, the same rate
    // as the rest of the tab, and skip it while the browser tab is hidden.
    const timer = window.setInterval(() => {
      if (!document.hidden) void read();
    }, 30_000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [window_]);

  // The window picker (1h, 24h, 7d, 30d). Each chart gets its own copy of
  // this row of buttons, but they all share one state, so pressing a button
  // in one row changes every chart (ADR-080). `where` names the row for
  // screen readers.
  const toolbar = (where: string, testPrefix: string) => (
    <p
      className={`${cardStyles.statusRow} op-seg op-seg-wrap`}
      role="group"
      aria-label={`Window for every chart, ${where}`}
    >
      {MACHINE_WINDOWS.map((option) => (
        <button
          key={option}
          type="button"
          className={cardStyles.back}
          aria-pressed={option === window_}
          onClick={() => {
            // Reset to loading here, where the window changes, not in the effect.
            // Clicking the window already shown must do nothing: the effect
            // would not re-run, and the cards would say "Loading" forever.
            if (option === window_) return;
            setWindow(option);
            setMachines(null);
          }}
          data-testid={`${testPrefix}-${option}`}
        >
          {windowName(option)}
        </button>
      ))}
    </p>
  );

  return { machines, latest, window: window_, toolbar };
}
// #endregion machines-read
