/**
 * Does:      Draws the blog's side column: the posts read most this week, from the list's first page, and where readers
 *            came from over the last 30 days, read from the Admin route into a box whose size is set before the read
 *            answers (ADR-113, the addendum on the blog holding still on a phone), empty until it does.
 * Does not:  Read the list (BlogPage hands its cards over) or keep any count.
 * Used by:   BlogPage.tsx.
 */
import { useEffect, useState } from 'react';
import {
  fetchBlogStats,
  mostRead,
  postDate,
  sourceShares,
  type BlogCard,
  type BlogStats,
} from '../../../lib/blog';
import styles from './BlogSide.module.css';

export function BlogSide({
  cards,
  onOpen,
}: {
  cards: BlogCard[] | null;
  onOpen: (slug: string) => void;
}) {
  const [stats, setStats] = useState<BlogStats | null>(null);
  useEffect(() => {
    const stop = new AbortController();
    fetchBlogStats(stop.signal)
      .then(setStats)
      .catch(() => {});
    return () => stop.abort();
  }, []);
  // Four rows at most, the four sources the site names, so the box has its size before its read answers.
  const shares = sourceShares(stats?.sources ?? []).slice(0, 4);
  return (
    <aside className={styles.side} aria-label="Most read and where readers came from">
      <section className={`${styles.box} op-glass`} data-testid="blog-most-read">
        <h2 className={styles.boxTitle}>Most read this week</h2>
        {cards === null || cards.length === 0 ? (
          <p className={styles.note}>Nothing read yet.</p>
        ) : (
          <ol className={styles.ranked}>
            {mostRead(cards).map((post) => (
              <li key={post.slug}>
                <button type="button" onClick={() => onOpen(post.slug)}>
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
        <div className={styles.sourcesBody} data-state={stats === null ? 'reading' : 'ready'}>
          {/* Not read yet is not "no reads": the body stays empty in its kept box until the read answers (ADR-142). */}
          {stats === null ? null : shares.length === 0 ? (
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
        </div>
      </section>
    </aside>
  );
}
