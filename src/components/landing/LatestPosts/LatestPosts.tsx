/**
 * Does:      Draws the home page's Latest posts band: the three newest posts from the blog as short cards, and a way to
 *            all of them. Draws nothing while the posts are read, when the blog cannot be read, or when it has none.
 * Does not:  Fetch on its own (useBlogPosts.ts reads the posts once for the page) or report a read.
 * Used by:   Landing.tsx.
 */
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { BlogMini } from '../../blog/BlogPage';
import styles from './LatestPosts.module.css';

/** How many of the newest posts the band shows. */
export const LATEST_COUNT = 3;

export function LatestPosts({
  headingClass,
  onOpenBlog,
}: {
  headingClass: string;
  onOpenBlog: (slug: string | null) => void;
}) {
  const posts = useBlogPosts();
  if (posts.state !== 'ready' || posts.posts.length === 0) return null;
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
        {posts.posts.slice(0, LATEST_COUNT).map((post) => (
          <BlogMini key={post.slug} post={post} onOpen={onOpenBlog} />
        ))}
      </div>
    </section>
  );
}
