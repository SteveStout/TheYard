/**
 * Does:      Reads a card's live ring at its source's grain while the card is open and the tab is in view, and stops
 *            for good once the card answers that it has no live source (ADR: Live dials where the source allows).
 * Does not:  Decide a figure or a tone: the server owns them; src/lib/live.ts says them.
 * Used by:   LivePanel.tsx.
 */
import { useEffect, useState } from 'react';
import { keepPolling, type LiveReport, pollMs, readLive } from '../lib/live';

export function useLive(key: string): LiveReport | null {
  const [report, setReport] = useState<LiveReport | null>(null);
  useEffect(() => {
    let alive = true;
    let timer: ReturnType<typeof setTimeout> | null = null;
    let last: LiveReport | null = null;
    const tick = async () => {
      timer = null;
      if (!keepPolling(alive, document.visibilityState, last)) return;
      const answer = await readLive(key);
      if (!alive) return;
      if (answer !== null) {
        last = answer;
        setReport(answer);
      }
      if (keepPolling(alive, document.visibilityState, last))
        timer = setTimeout(() => void tick(), pollMs(last));
    };
    const onVisible = () => {
      if (document.visibilityState === 'visible' && timer === null) void tick();
    };
    void tick();
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      alive = false;
      if (timer !== null) clearTimeout(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [key]);
  return report;
}
