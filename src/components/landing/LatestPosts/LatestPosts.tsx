/**
 * Does:      Draws the three newest posts from the blog in one of two forms. The home page's Latest posts band: the
 *            shared section head with its 02 badge and "All posts" on its right, then short cards. The review route's row (ADR: The review route gives a reason before it
 *            asks, the addendum on named voices and the blog): the section heading the page hands in with "All posts",
 *            then three cards three across from 1024 up, each one link to its post with its cover, title, two lines of
 *            summary, date and reading time, every press reported. Draws nothing while the posts are read, when the
 *            blog cannot be read, or when it has none.
 * Does not:  Fetch on its own (useBlogPosts.ts reads the posts once for the page) or report a read.
 * Used by:   Landing.tsx, ReviewPage.tsx.
 */
import type { MouseEvent, ReactNode } from 'react';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { ALL_POSTS_ADDRESS, postDate, postLink } from '../../../lib/blog';
import { minutesLine } from '../../../lib/blogFilter';
import { SECTION_NUMBERS } from '../../../lib/landingMoves';
import { BlogMini } from '../../blog/BlogMini';
import { SectionHead } from '../../shared/SectionHead';
import styles from './LatestPosts.module.css';

/** How many of the newest posts the band shows. */
export const LATEST_COUNT = 3;

/** A plain click, which the page answers itself; any other keeps the browser's own meaning. */
const plainClick = (event: MouseEvent) =>
  !(event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0);

export function LatestPosts({
  onOpenBlog,
  row,
}: {
  onOpenBlog: (slug: string | null) => void;
  /** The review route's row: its heading, handed the "All posts" link to place, and the report of every press. */
  row?: { head: (all: ReactNode) => ReactNode; onFollow: () => void };
}) {
  const posts = useBlogPosts();
  // The review row's holder of its number badge's one pass of light.
  const [holder] = useFirstSeen<HTMLElement>();
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
      <section
        ref={holder}
        id="blog"
        aria-labelledby="review-blog-title"
        data-mock="blog"
        data-testid="review-blog"
      >
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
    <section
      className={styles.band}
      aria-labelledby="landing-latest"
      data-mock="posts"
      data-testid="landing-latest"
    >
      <SectionHead
        id="landing-latest"
        number={SECTION_NUMBERS.posts}
        title="Latest posts"
        meta={
          <button type="button" className={styles.all} onClick={() => onOpenBlog(null)}>
            All posts <span aria-hidden="true">→</span>
          </button>
        }
        seen
      />
      <div className={styles.cards}>
        {newest.map((post) => (
          <BlogMini key={post.slug} post={post} onOpen={onOpenBlog} />
        ))}
      </div>
    </section>
  );
}
