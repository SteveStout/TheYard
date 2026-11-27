/**
 * Does:      Holds one filter's list for the blog page (ADR: Every LinkedIn post is also a blog post, the addendum on the
 *            read model): the first page as the rendering service drew it when it matches, then each change read
 *            through listReader (the search box waits 300 ms, everything else asks at once, a newer ask aborts the
 *            older), the old list kept on screen and marked as reading until the new one answers, and the pages after it,
 *            read through the same reader so a newer ask drops them, and kept when a post is opened and Back comes home.
 * Does not:  Decide the filter (the address does, through BlogPage) or draw anything.
 * Used by:   BlogPage.tsx.
 */
import { startTransition, useEffect, useRef, useState } from 'react';
import { fetchList, type BlogList } from '../lib/blog';
import { listReader, type AskWhen } from '../lib/blogReads';
import { listPath, NO_FILTER, type BlogFilter } from '../lib/blogFilter';
import { postsOnce } from './useBlogPosts';

/** What the page draws from: the list (null before the first answer), whether a newer one is being read, and why not. */
export type BlogListState = {
  list: BlogList | null;
  reading: boolean;
  unavailable: boolean;
  failed: boolean;
  /** Reads the next page and adds its cards; nothing when there is none or one is being read. */
  more: () => void;
  /**
   * Called on each key in the search box with the filter it leads to, before its transition, so a waiting search
   * waits from the last key and never asks for a word the box has already typed past.
   */
  typed: (next: BlogFilter) => void;
  moreReading: boolean;
};

/** One filter's list, read as the filter changes; first is the rendering service's answer for the address it drew. */
export function useBlogList(
  filter: BlogFilter,
  when: AskWhen,
  first: BlogList | null | undefined,
  active: boolean
): BlogListState {
  const path = listPath(filter);
  const drawnFor = useRef(first ? path : null);
  const [list, setList] = useState<BlogList | null>(first ?? null);
  const [shownFor, setShownFor] = useState<string | null>(first ? path : null);
  const [unavailable, setUnavailable] = useState(false);
  const [failed, setFailed] = useState(false);
  const [moreReading, setMoreReading] = useState(false);
  const reader = useRef<ReturnType<typeof listReader> | null>(null);

  useEffect(() => {
    // The list with nothing filtered is the page-load's one shared read (useBlogPosts): the page does not read it twice.
    const read = (asked: string, signal: AbortSignal) =>
      asked === listPath(NO_FILTER) ? postsOnce() : fetchList(asked, signal);
    // An answer is drawn as a transition, so React draws a new list in slices and a key pressed meanwhile is never
    // held behind it (the budget: no task over 50 ms while a search is typed).
    reader.current = listReader(read, (asked, answer, wasFailure) =>
      startTransition(() => {
        setShownFor(asked);
        setList(answer);
        setUnavailable(answer === null && !wasFailure);
        setFailed(wasFailure);
        setMoreReading(false);
      })
    );
    return () => reader.current?.stop();
  }, []);

  useEffect(() => {
    if (!active) return;
    // The page the service drew is the first answer: the browser does not ask for it again.
    if (drawnFor.current === path) {
      drawnFor.current = null;
      return;
    }
    drawnFor.current = null;
    // Back from a post comes home to the list it left, every page it had read included: nothing to ask again.
    if (shownFor === path && list !== null) return;
    reader.current?.ask(path, list === null ? 'now' : when);
    // when changes with path, and the list and what it shows are read only to know whether anything is on screen yet.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, active]);

  const more = () => {
    if (!list?.next || moreReading) return;
    setMoreReading(true);
    const shown = list;
    reader.current?.more(listPath(filter, list.next), (page) => {
      if (page) setList({ ...page, cards: [...shown.cards, ...page.cards] });
      setMoreReading(false);
    });
  };

  const typed = (next: BlogFilter) => reader.current?.typed(listPath(next));

  return { list, reading: shownFor !== path, unavailable, failed, more, moreReading, typed };
}
