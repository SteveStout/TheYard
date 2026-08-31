import { useEffect, useState } from 'react';

/**
 * The current time as epoch ms, re-rendering on an interval. One instance at
 * the app root keeps every countdown and auction status on the same clock.
 */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);

  return now;
}
