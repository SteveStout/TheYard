/**
 * Does:      Holds the one post the blog page shows in full: the rendering service's when it is that post, otherwise read
 *            from its own route, the read for a post the page has moved past aborted.
 * Does not:  Hold the list (useBlogList.ts does) or report a read (useBlogReport.ts does).
 * Used by:   BlogPage.tsx.
 */
import { startTransition, useEffect, useState } from 'react';
import { fetchPost, type BlogPost } from '../lib/blog';

/** The post in full, whether it is still being read, and whether no published post has the slug. */
export type BlogPostState = { post: BlogPost | null; reading: boolean; missing: boolean };

/** The post a slug names; null slug for none. first is the post the rendering service drew. */
export function useBlogPost(
  slug: string | null,
  first: BlogPost | null | undefined
): BlogPostState {
  const [held, setHeld] = useState<{ slug: string; post: BlogPost | null } | null>(
    first ? { slug: first.slug, post: first } : null
  );
  useEffect(() => {
    if (slug === null || held?.slug === slug) return;
    const stop = new AbortController();
    fetchPost(slug, stop.signal)
      .then((post) => startTransition(() => setHeld({ slug, post })))
      .catch(() => {
        if (!stop.signal.aborted) setHeld({ slug, post: null });
      });
    return () => stop.abort();
    // held is what this effect writes; reading it here only skips a post already held.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug]);
  const here = slug !== null && held?.slug === slug;
  return {
    post: here ? held.post : null,
    reading: slug !== null && !here,
    missing: here && held.post === null,
  };
}
