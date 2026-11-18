/**
 * Does:      Reads the blog's list so the database sees only the reads that matter (ADR: Every LinkedIn post is also a
 *            blog post, the addendum on the read model): a search waits 300 ms after the last key, a chip or a choice
 *            asks at once, and each new ask aborts the read it replaces, whose answer is then never handed on.
 * Does not:  Know what a filter is (blogFilter.ts writes the address it asks for) or hold the answer (useBlogList does).
 * Used by:   useBlogList.ts.
 */
import type { BlogList } from './blog';

/** How long a search waits after the last key before it asks. */
export const SEARCH_WAIT_MS = 300;

/** wait for the search box's keys; now for a chip, a select, Clear, a page, Back and Forward. */
export type AskWhen = 'wait' | 'now';

/** Hands on a read's answer: the list, or null with failed false when the blog cannot be read, or null with failed true when the read failed. */
export type OnAnswer = (path: string, answer: BlogList | null, failed: boolean) => void;

/** A reader that debounces the search box and aborts what it replaces. */
export function listReader(
  read: (path: string, signal: AbortSignal) => Promise<BlogList | null>,
  onAnswer: OnAnswer
) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: AbortController | null = null;

  const start = (path: string) => {
    inFlight?.abort();
    const stop = new AbortController();
    inFlight = stop;
    read(path, stop.signal).then(
      (answer) => {
        if (!stop.signal.aborted) onAnswer(path, answer, false);
      },
      () => {
        if (!stop.signal.aborted) onAnswer(path, null, true);
      }
    );
  };

  return {
    /** Asks for a list address, now or after the search's wait; a pending ask or a read under way gives way. */
    ask(path: string, when: AskWhen) {
      clearTimeout(timer);
      if (when === 'now') start(path);
      else timer = setTimeout(() => start(path), SEARCH_WAIT_MS);
    },
    /** Drops a pending ask and aborts the read under way: the page is going. */
    stop() {
      clearTimeout(timer);
      inFlight?.abort();
      inFlight = null;
    },
  };
}
