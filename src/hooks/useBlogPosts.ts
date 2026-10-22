/**
 * Does:      Reads the blog's posts once per page load and shares them with every reader of them: the side nav's NEW POST,
 *            the home page's Latest posts band and the blog page itself.
 * Does not:  Send a read or an event (useBlogReport.ts does), or draw anything.
 * Used by:   Shell.tsx, LatestPosts.tsx, BlogPage.tsx.
 */
import { useEffect, useState } from 'react';
import { fetchPosts, isNew, type BlogPost } from '../lib/blog';

/** What the reader of the posts sees: still reading, the posts newest first, or the blog unreadable now. */
export type BlogPosts =
  | { state: 'loading' }
  | { state: 'ready'; posts: BlogPost[]; readAt: number }
  | { state: 'unavailable' };

let shared: Promise<BlogPost[] | null> | null = null;

/** The one read of the posts a page load makes, however many places ask; a failed read is asked again next time. */
function postsOnce(): Promise<BlogPost[] | null> {
  shared ??= fetchPosts().catch(() => {
    shared = null;
    return null;
  });
  return shared;
}

/** The blog's posts, read once per page load and shared. */
export function useBlogPosts(): BlogPosts {
  const [posts, setPosts] = useState<BlogPosts>({ state: 'loading' });
  useEffect(() => {
    let live = true;
    void postsOnce().then((read) => {
      if (live)
        setPosts(
          read === null
            ? { state: 'unavailable' }
            : { state: 'ready', posts: read, readAt: Date.now() }
        );
    });
    return () => {
      live = false;
    };
  }, []);
  return posts;
}

/** True while the newest post is under a week old, judged when the posts arrived: the side nav's NEW POST. */
export function newPost(posts: BlogPosts): boolean {
  return posts.state === 'ready' && isNew(posts.posts[0]?.published_at, posts.readAt);
}
