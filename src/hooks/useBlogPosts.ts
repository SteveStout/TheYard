/**
 * Does:      Reads the blog's list once per page load, its first page with nothing filtered, and shares it with every
 *            reader of it: the side nav's NEW POST, the home page's Latest posts band, and the blog page's most-read box,
 *            its count of every post and its chips' labels.
 * Does not:  Send a read or an event (useBlogReport.ts does), or draw anything.
 * Used by:   Shell.tsx, LatestPosts.tsx, BlogPage.tsx, useBlogList.ts.
 */
import { useEffect, useState } from 'react';
import { fetchPosts, isNew, type BlogCard, type BlogList } from '../lib/blog';
import { filterFromSearch, isFiltered } from '../lib/blogFilter';
import { useFirstLoad } from './useFirstLoad';

/** What the reader of the posts sees: still reading, the newest posts' cards and the whole list's answer, or the blog unreadable now. */
export type BlogPosts =
  | { state: 'loading' }
  | { state: 'ready'; posts: BlogCard[]; list: BlogList; readAt: number }
  | { state: 'unavailable' };

let shared: Promise<BlogList | null> | null = null;

/**
 * The one read of the list's first page a page load makes, however many places ask; a failed read is asked again next
 * time. A blog page the rendering service drew with nothing filtered already holds that page, and it is not read again.
 */
export function postsOnce(drawn: BlogList | null = null): Promise<BlogList | null> {
  if (drawn) shared ??= Promise.resolve(drawn);
  shared ??= fetchPosts().catch(() => {
    shared = null;
    return null;
  });
  return shared;
}

/** The blog's posts, read once per page load and shared. */
export function useBlogPosts(): BlogPosts {
  const first = useFirstLoad();
  const drawn =
    first?.blog?.list && !isFiltered(filterFromSearch(first.search)) ? first.blog.list : null;
  const [posts, setPosts] = useState<BlogPosts>({ state: 'loading' });
  useEffect(() => {
    let live = true;
    void postsOnce(drawn).then((read) => {
      if (live)
        setPosts(
          read === null
            ? { state: 'unavailable' }
            : { state: 'ready', posts: read.cards, list: read, readAt: Date.now() }
        );
    });
    return () => {
      live = false;
    };
    // The first load is the page's own and never changes after it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return posts;
}

/** True while the newest post is under a week old, judged when the posts arrived: the side nav's NEW POST. */
export function newPost(posts: BlogPosts): boolean {
  return posts.state === 'ready' && isNew(posts.posts[0]?.published_at, posts.readAt);
}
