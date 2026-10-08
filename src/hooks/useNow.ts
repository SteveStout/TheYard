import { useEffect, useState } from 'react';

/**
 * The current time as epoch ms, re-rendering on an interval. One instance at
 * the app root keeps every countdown and auction status on the same clock.
 * A page drawn on a server starts from the server's clock, so the first draw
 * here writes the countdowns the server wrote, and the next tick moves them on.
 * @param startMs the clock the page was drawn at, when it was drawn elsewhere
 */
export function useNow(intervalMs = 1000, startMs?: number): number {
  const [now, setNow] = useState(() => startMs ?? Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);

  return now;
}
