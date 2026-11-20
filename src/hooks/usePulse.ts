/**
 * Does:      Reads /api/pulse for a page, the one way the site reads it (ADR: One reading for the first screen): from the
 *            reading the rendering service drew the page with, then again at once and every fifteen seconds while the
 *            page is open, never while the tab is hidden, each read in the background so it never shows the working
 *            mark. A failed read keeps the last answer and says so (nowPanel.ts, nextReading).
 * Does not:  Draw anything or turn a figure into words (pulse.ts and nowPanel.ts do).
 * Used by:   PulseBand.tsx, ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import { nextReading, type PulseReading } from '../lib/nowPanel';
import { PULSE_EVERY_MS, PULSE_URL, pulseFrom } from '../lib/pulse';
import { request } from '../lib/request';
import { useFirstLoad } from './useFirstLoad';

export function usePulse(): PulseReading {
  // A page the rendering service drew carries the reading it drew from, so the browser's first draw matches it.
  const first = useFirstLoad();
  const [reading, setReading] = useState<PulseReading>(() => ({
    pulse: pulseFrom(first?.pulse ?? null),
    failed: false,
  }));
  useEffect(() => {
    let live = true;
    const read = () => {
      if (document.hidden) return;
      request(PULSE_URL, undefined, { background: true })
        .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
        .catch(() => null)
        .then((answer) => {
          if (live) setReading((last) => nextReading(last, answer));
        });
    };
    read();
    const timer = window.setInterval(read, PULSE_EVERY_MS);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, []);
  return reading;
}
