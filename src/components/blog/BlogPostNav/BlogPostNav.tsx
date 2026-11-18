/**
 * Does:      Draws the foot of a single post: the older and the newer post as two links side by side, and up to three
 *            posts that share a topic with it as short cards (ADR: Every LinkedIn post is also a blog post, the
 *            addendum on the read model).
 * Does not:  Work out which posts those are (the API's post route does) or fetch them.
 * Used by:   BlogPage.tsx.
 */
import type { BlogPost } from '../../../lib/blog';
import type { FilterLabels } from '../../../lib/blogFilter';
import { BlogMini } from '../BlogMini';
import styles from './BlogPostNav.module.css';

export function BlogPostNav({
  post,
  labels,
  onOpen,
  onOpenSlides,
  onTopic,
  onKeyword,
}: {
  post: BlogPost;
  labels: FilterLabels | null;
  onOpen: (slug: string) => void;
  onOpenSlides: (slug: string) => void;
  onTopic: (key: string) => void;
  onKeyword: (key: string) => void;
}) {
  return (
    <>
      <nav className={styles.around} aria-label="Older and newer posts" data-testid="blog-post-nav">
        {post.older ? (
          <button
            type="button"
            className={styles.step}
            onClick={() => onOpen(post.older!.slug)}
            data-testid="blog-older"
          >
            <small>← Older</small>
            {post.older.title}
          </button>
        ) : (
          <span />
        )}
        {post.newer ? (
          <button
            type="button"
            className={`${styles.step} ${styles.newer}`}
            onClick={() => onOpen(post.newer!.slug)}
            data-testid="blog-newer"
          >
            <small>Newer →</small>
            {post.newer.title}
          </button>
        ) : (
          <span />
        )}
      </nav>
      {post.related.length > 0 && (
        <section
          className={styles.related}
          aria-labelledby="blog-related"
          data-testid="blog-related"
        >
          <h2 id="blog-related" className={styles.relatedTitle}>
            Related by topic
          </h2>
          {post.related.map((card) => (
            <BlogMini
              key={card.slug}
              post={card}
              onOpen={onOpen}
              onOpenSlides={onOpenSlides}
              labels={labels}
              onTopic={onTopic}
              onKeyword={onKeyword}
            />
          ))}
        </section>
      )}
    </>
  );
}
