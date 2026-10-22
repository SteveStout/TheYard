/**
 * Does:      Draws the blog at ?view=blog (ADR: Every LinkedIn post is also a blog post): the head, the newest post in full
 *            and the older ones as short cards, or the one post an address names; beside them the most read this week
 *            and where readers came from.
 * Does not:  Keep the posts (useBlogPosts.ts reads them once) or report a read (BlogPostCard does).
 * Used by:   App.tsx.
 */
import { useEffect, useState } from 'react';
import {
  fetchBlogStats,
  mostRead,
  postDate,
  sourceShares,
  type BlogPost,
  type BlogStats,
} from '../../../lib/blog';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { BlogPostCard } from '../BlogPostCard';
import styles from './BlogPage.module.css';

/** The short card an older post gets in the list, and a post gets in the home page's band. */
export function BlogMini({ post, onOpen }: { post: BlogPost; onOpen: (slug: string) => void }) {
  return (
    <article className={`${styles.mini} op-glass op-tile`} data-testid="blog-mini">
      <h3 className={styles.miniTitle}>{post.title}</h3>
      <p className={styles.miniSummary}>{post.summary}</p>
      <p className={styles.miniFoot}>
        <time dateTime={post.published_at}>{postDate(post.published_at)}</time>
        <span>{post.reads} reads</span>
        <button type="button" className={styles.read} onClick={() => onOpen(post.slug)}>
          Read the post <span aria-hidden="true">→</span>
        </button>
      </p>
    </article>
  );
}

export function BlogPage({
  slug,
  onOpenPost,
  onAllPosts,
}: {
  slug: string | null;
  onOpenPost: (slug: string) => void;
  onAllPosts: () => void;
}) {
  const posts = useBlogPosts();
  const [stats, setStats] = useState<BlogStats | null>(null);
  useEffect(() => {
    const stop = new AbortController();
    fetchBlogStats(stop.signal)
      .then(setStats)
      .catch(() => {});
    return () => stop.abort();
  }, []);

  const list = posts.state === 'ready' ? posts.posts : [];
  const named = slug === null ? null : (list.find((post) => post.slug === slug) ?? null);
  const tags = [...new Set(list.flatMap((post) => post.tags))].slice(0, 6);
  const shares = sourceShares(stats?.sources ?? []);

  return (
    <section className={styles.page} aria-labelledby="blog-title" data-testid="blog-page">
      <div className={styles.head}>
        <h1 id="blog-title" className={styles.title}>
          Blog
        </h1>
        {tags.length > 0 && (
          <ul className={styles.tags} aria-label="Topics">
            {tags.map((tag) => (
              <li key={tag}>#{tag}</li>
            ))}
          </ul>
        )}
        {slug !== null && (
          <button type="button" className={styles.back} onClick={onAllPosts}>
            All posts
          </button>
        )}
      </div>
      <p className={styles.intro}>
        Everything I post on LinkedIn, kept here in full with the code and the live pages it talks
        about. Newest first.
      </p>

      <div className={styles.layout}>
        <div className={styles.list} data-testid="blog-list">
          {posts.state === 'loading' && <p className={styles.note}>Reading the posts...</p>}
          {posts.state === 'unavailable' && (
            <p className={styles.note}>The blog cannot be read now. The rest of the site can.</p>
          )}
          {posts.state === 'ready' && list.length === 0 && (
            <p className={styles.note}>No posts yet.</p>
          )}
          {posts.state === 'ready' && slug !== null && named === null && (
            <p className={styles.note}>No post has that address. Every post is listed below.</p>
          )}
          {named !== null ? (
            <BlogPostCard key={named.slug} post={named} />
          ) : (
            list.map((post, index) =>
              index === 0 ? (
                <BlogPostCard key={post.slug} post={post} />
              ) : (
                <BlogMini key={post.slug} post={post} onOpen={onOpenPost} />
              )
            )
          )}
        </div>

        <aside className={styles.side} aria-label="Most read and where readers came from">
          <section className={`${styles.box} op-glass`} data-testid="blog-most-read">
            <h2 className={styles.boxTitle}>Most read this week</h2>
            {list.length === 0 ? (
              <p className={styles.note}>Nothing read yet.</p>
            ) : (
              <ol className={styles.ranked}>
                {mostRead(list).map((post) => (
                  <li key={post.slug}>
                    <button type="button" onClick={() => onOpenPost(post.slug)}>
                      {post.title}
                    </button>
                    <small>
                      {post.reads7d} reads · {postDate(post.published_at)}
                    </small>
                  </li>
                ))}
              </ol>
            )}
          </section>
          <section className={`${styles.box} op-glass`} data-testid="blog-sources">
            <h2 className={styles.boxTitle}>Where readers came from</h2>
            {shares.length === 0 ? (
              <p className={styles.note}>No reads in the last 30 days yet.</p>
            ) : (
              <dl className={styles.sources}>
                {shares.map((share) => (
                  <div key={share.source} className={styles.source}>
                    <dt>{share.label}</dt>
                    <dd>
                      <span className={styles.meter} aria-hidden="true">
                        <i style={{ width: `${share.share}%` }} />
                      </span>
                      {share.share}%
                    </dd>
                  </div>
                ))}
              </dl>
            )}
          </section>
        </aside>
      </div>
    </section>
  );
}
