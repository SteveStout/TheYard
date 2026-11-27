/**
 * Does:      Draws Why I write on the About page, the bridge from the personal rows back to the close (ADR-142, the
 *            addendum on the landing views; his own line, word for word, from the author document): the heading and
 *            his line on the left, and on the right the newest post as a card, its cover, title, "Newest post", its
 *            date and read time and "Read it", opening the post at its own address. The newest post is the list every
 *            page shares, which the server's first draw carries, so the card is never an empty box.
 * Does not:  Hold the line (the document does, aboutWeave.ts cuts it out) or read the posts (useBlogPosts.ts does).
 * Used by:   AboutPage.tsx.
 */
import type { Write } from '../../../lib/aboutWeave';
import { postDate } from '../../../lib/blog';
import { minutesLine } from '../../../lib/blogFilter';
import { words } from '../../../lib/aboutWeave';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import styles from './AboutWrite.module.css';

export function AboutWrite({ write }: { write: Write }) {
  const posts = useBlogPosts();
  const newest = posts.state === 'ready' ? (posts.posts[0] ?? null) : null;
  return (
    <section
      className={`op-glass ${styles.row}`}
      aria-labelledby="about-write-title"
      data-mock="why-write"
    >
      <div>
        <h3 id="about-write-title" className={styles.title}>
          {write.title}
        </h3>
        <p className={styles.line} data-mock="write-line">
          {words(write.body)}
        </p>
      </div>
      <div className={styles.slot}>
        {newest && (
          <a
            className={`op-glass ${styles.latest}`}
            href={`/?view=blog&post=${newest.slug}`}
            data-mock="latest-post"
            data-testid="about-latest-post"
          >
            {newest.cover && (
              <img
                src={newest.cover.thumb_url}
                width={newest.cover.width}
                height={newest.cover.height}
                alt=""
                loading="lazy"
              />
            )}
            <span>
              <strong>{newest.title}</strong>
              <span className={styles.meta}>
                Newest post · {postDate(newest.published_at)} ·{' '}
                {minutesLine(newest.reading_minutes)}
              </span>
              <span className={styles.read}>Read it</span>
            </span>
          </a>
        )}
      </div>
    </section>
  );
}
