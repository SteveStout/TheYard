/**
 * Does:      Draws the home page's newest post on its first screen, under the chart and its link, as the picture draws
 *            it: one link to the post with its badge ("New post" while it is under a week old, else "Latest post"), its
 *            title and its reading time. Its row keeps its height while the posts are read, so nothing under it moves
 *            when it fills; with no post, or a read that fails, it is not drawn.
 * Does not:  Read the posts on its own (useBlogPosts.ts reads them once for the page, the same read as Latest posts).
 * Used by:   Landing.tsx.
 */
import type { MouseEvent } from 'react';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { postLink } from '../../../lib/blog';
import { minutesLine } from '../../../lib/blogFilter';
import { newestBadge } from '../../../lib/landingMoves';
import styles from './NewestPost.module.css';

export function NewestPost({ onOpenBlog }: { onOpenBlog: (slug: string) => void }) {
  const posts = useBlogPosts();
  if (posts.state === 'loading')
    return (
      <div className={styles.waiting} aria-hidden="true" data-testid="landing-newest-waiting" />
    );
  if (posts.state !== 'ready' || posts.posts.length === 0) return null;
  const post = posts.posts[0];
  const open = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0)
      return;
    event.preventDefault();
    onOpenBlog(post.slug);
  };
  return (
    <a
      className={`${styles.post} op-glass op-tile`}
      href={postLink(post.slug)}
      onClick={open}
      data-testid="landing-newest"
    >
      <span className={styles.badge}>{newestBadge(post.published_at, posts.readAt)}</span>
      <span className={styles.title}>{post.title}</span>
      <span className={styles.meta}>{minutesLine(post.reading_minutes)}</span>
    </a>
  );
}
