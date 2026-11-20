/**
 * Does:      Draws the three newest posts from the blog in one of two forms. The home page's Latest posts band: short
 *            cards and a way to all of them. The review route's row (ADR: The review route gives a reason before it
 *            asks, the addendum on named voices and the blog): the section heading the page hands in with "All posts",
 *            then three cards three across from 1024 up, each one link to its post with its cover, title, two lines of
 *            summary, date and reading time, every press reported. Draws nothing while the posts are read, when the
 *            blog cannot be read, or when it has none.
 * Does not:  Fetch on its own (useBlogPosts.ts reads the posts once for the page) or report a read.
 * Used by:   Landing.tsx, ReviewPage.tsx.
 */
import type { MouseEvent, ReactNode } from 'react';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { ALL_POSTS_ADDRESS, postDate, postLink } from '../../../lib/blog';
import { minutesLine } from '../../../lib/blogFilter';
import { BlogMini } from '../../blog/BlogMini';
import styles from './LatestPosts.module.css';

/** How many of the newest posts the band shows. */
export const LATEST_COUNT = 3;

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function LatestPosts({
  headingClass = '',
  onOpenBlog,
  row,
}: {
  headingClass?: string;
  onOpenBlog: (slug: string | null) => void;
  /** The review route's row: its heading, handed the "All posts" link to place, and the report of every press. */
  row?: { head: (all: ReactNode) => ReactNode; onFollow: () => void };
}) {
  const posts = useBlogPosts();
  if (posts.state !== 'ready' || posts.posts.length === 0) return null;
  const newest = posts.posts.slice(0, LATEST_COUNT);
  if (row) {
    const open = (slug: string | null) => (event: MouseEvent<HTMLAnchorElement>) => {
      row.onFollow();
      if (!plainClick(event)) return;
      event.preventDefault();
      onOpenBlog(slug);
    };
    return (
      <section aria-labelledby="review-blog-title" data-testid="review-blog">
        {row.head(
          <a
            className={styles.allLink}
            href={ALL_POSTS_ADDRESS}
            onClick={open(null)}
            data-action="review.blog"
            data-testid="review-blog-all"
          >
            All posts
          </a>
        )}
        <div className={styles.posts}>
          {newest.map((post) => (
            <a
              key={post.slug}
              className={`${styles.post} op-glass op-tile`}
              href={postLink(post.slug)}
              onClick={open(post.slug)}
              data-action="review.blog"
              data-testid="review-blog-post"
            >
              {post.cover && (
                <img
                  src={post.cover.thumb_url}
                  alt=""
                  width={72}
                  height={90}
                  loading="lazy"
                  decoding="async"
                />
              )}
              <span className={styles.text}>
                <strong className={styles.title} data-testid="review-blog-title">
                  {post.title}
                </strong>
                <span className={styles.summary}>{post.summary}</span>
                <span className={styles.foot}>
                  <time dateTime={post.published_at}>{postDate(post.published_at)}</time>
                  <span>{minutesLine(post.reading_minutes)}</span>
                  <span className={styles.go}>Read the post</span>
                </span>
              </span>
            </a>
          ))}
        </div>
      </section>
    );
  }
  return (
    <section className={styles.band} aria-labelledby="landing-latest" data-testid="landing-latest">
      <div className={styles.head}>
        <h2 id="landing-latest" className={headingClass}>
          Latest posts
        </h2>
        <button type="button" className={styles.all} onClick={() => onOpenBlog(null)}>
          All posts <span aria-hidden="true">→</span>
        </button>
      </div>
      <div className={styles.cards}>
        {newest.map((post) => (
          <BlogMini key={post.slug} post={post} onOpen={onOpenBlog} />
        ))}
      </div>
    </section>
  );
}
