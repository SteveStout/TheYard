/**
 * Does:      Reads the blog's list so the database sees only the reads that matter (ADR: Every LinkedIn post is also a
 *            blog post, the addendum on the read model): a search waits 300 ms after the last key, a chip or a choice
 *            asks at once, and each new ask aborts the read it replaces, whose answer is then never handed on; a next
 *            page is read under the same rule, so a page asked for the old list never lands on a newer one.
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
  let waiting: string | null = null;
  let inFlight: AbortController | null = null;
  let nextInFlight: AbortController | null = null;

  const dropNext = () => {
    nextInFlight?.abort();
    nextInFlight = null;
  };

  const start = (path: string) => {
    dropNext();
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

  const wait = (path: string) => {
    clearTimeout(timer);
    waiting = path;
    timer = setTimeout(() => {
      waiting = null;
      start(path);
    }, SEARCH_WAIT_MS);
  };

  return {
    /** Asks for a list address, now or after the search's wait; a pending ask or a read under way gives way. */
    ask(path: string, when: AskWhen) {
      clearTimeout(timer);
      waiting = null;
      if (when === 'now') start(path);
      else wait(path);
    },
    /**
     * A key was pressed: a search still waiting waits its whole time again from this key. Its address arrives only
     * when the page's transition commits, and a later key can hold that commit back, so without this the wait ran
     * from the last page drawn and a slow machine asked for "c" while "copilot" was still being typed.
     */
    typed() {
      if (waiting !== null) wait(waiting);
    },
    /**
     * Reads the list's next page and hands it on, or null when it failed; a newer ask or stop aborts it first, and a
     * page aborted that way is never handed on.
     */
    more(path: string, onPage: (page: BlogList | null) => void) {
      dropNext();
      const stop = new AbortController();
      nextInFlight = stop;
      read(path, stop.signal).then(
        (page) => {
          if (!stop.signal.aborted) onPage(page);
        },
        () => {
          if (!stop.signal.aborted) onPage(null);
        }
      );
    },
    /** Drops a pending ask and aborts the reads under way: the page is going. */
    stop() {
      clearTimeout(timer);
      waiting = null;
      inFlight?.abort();
      inFlight = null;
      dropNext();
    },
  };
}
