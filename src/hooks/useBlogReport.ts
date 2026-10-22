/**
 * Does:      Reports what a reader does with a post that is open in full (ADR: Every LinkedIn post is also a blog post):
 *            the read once, each scroll depth once, the seconds on the page when it is left, and the one-off events
 *            the card hands it (see more, a slide seen, a click out, a copy).
 * Does not:  Keep anything about the reader: the API turns the request into the day's keyed hash and a device class.
 * Used by:   BlogPostCard.tsx.
 */
import { useCallback, useEffect, useRef, type RefObject } from 'react';
import { depthsPassed, readReport, sendBlogReport } from '../lib/blog';

/** Where the visitor arrived from, taken once: the first post opened carries the tagged code and the referrer. */
let arrival: { search: string; referrer: string } | null =
  typeof window === 'undefined'
    ? null
    : { search: window.location.search, referrer: document.referrer };

/** The posts whose read this page load has sent: one read per post per load, however often its card mounts. */
const readSent = new Set<string>();

function takeArrival(): { search: string; referrer: string } {
  const first = arrival ?? { search: '', referrer: '' };
  arrival = { search: '', referrer: '' };
  return first;
}

/** One event for this post: depth, time, more, slide, click or copy, with its value. */
export type BlogEventSender = (type: string, value: string) => void;

/** Sends the read and the reading events for one post while `card` is on the page; hands back the one-off sender. */
export function useBlogReport(slug: string, card: RefObject<HTMLElement | null>): BlogEventSender {
  const sent = useRef(new Set<string>());
  const send = useCallback<BlogEventSender>(
    (type, value) => {
      const key = `${type}:${value}`;
      if (sent.current.has(key)) return;
      sent.current.add(key);
      sendBlogReport('/api/blog/events', { slug, type, value });
    },
    [slug]
  );

  useEffect(() => {
    if (!readSent.has(slug)) {
      readSent.add(slug);
      const { search, referrer } = takeArrival();
      sendBlogReport('/api/blog/reads', readReport(slug, search, referrer));
    }
    const opened = Date.now();
    const depths = new Set<number>();

    const onScroll = () => {
      const box = card.current?.getBoundingClientRect();
      if (!box || box.height <= 0) return;
      const fraction = Math.min(1, Math.max(0, (window.innerHeight - box.top) / box.height));
      for (const mark of depthsPassed(fraction, depths)) {
        depths.add(mark);
        send('depth', String(mark));
      }
    };
    let timed = false;
    const onLeave = () => {
      const seconds = Math.round((Date.now() - opened) / 1000);
      if (timed || seconds < 1) return;
      timed = true;
      send('time', String(Math.min(seconds, 86_400)));
    };
    const onHidden = () => {
      if (document.visibilityState === 'hidden') onLeave();
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    document.addEventListener('visibilitychange', onHidden);
    return () => {
      window.removeEventListener('scroll', onScroll);
      document.removeEventListener('visibilitychange', onHidden);
      onLeave();
    };
  }, [slug, card, send]);

  return send;
}
