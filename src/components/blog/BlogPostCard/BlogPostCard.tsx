/**
 * Does:      Draws one post in full, in the feed layout readers know from LinkedIn and in the site's own colours: who wrote
 *            it and when, the title and the text clamped with "see more", the slides, the reads, and three actions
 *            (comment on LinkedIn, read the code, copy the link). Reports the read and what the reader does.
 * Does not:  Fetch the post (BlogPage hands it over) or keep anything about the reader (useBlogReport.ts sends, the API keeps).
 * Used by:   BlogPage.tsx.
 */
import { useEffect, useRef, useState } from 'react';
import { bodyParts, postDate, type BlogPost } from '../../../lib/blog';
import { useBlogReport } from '../../../hooks/useBlogReport';
import styles from './BlogPostCard.module.css';

/** The author's line, the resume's own words. */
const AUTHOR = {
  initials: 'SS',
  name: 'Steven Stout',
  headline:
    'Lead / Staff .NET Engineer | 12 Years Full Stack .NET, 7 Fully Remote | Cloud Platform Architecture',
};

/** The address a copied link carries: the post on the blog, with no tagged code. */
function postAddress(slug: string): string {
  const origin = typeof window === 'undefined' ? '' : window.location.origin;
  return `${origin}/?view=blog&post=${slug}`;
}

export function BlogPostCard({ post }: { post: BlogPost }) {
  const card = useRef<HTMLElement>(null);
  const strip = useRef<HTMLOListElement>(null);
  const send = useBlogReport(post.slug, card);
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);

  // #region slides-seen
  // Each slide counts once, when most of it has been in the strip's view.
  useEffect(() => {
    const list = strip.current;
    if (!list || typeof IntersectionObserver === 'undefined') return;
    const watch = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          const position = (entry.target as HTMLElement).dataset.position;
          if (entry.isIntersecting && position) send('slide', position);
        }
      },
      { root: list, threshold: 0.6 }
    );
    list.querySelectorAll('li').forEach((slide) => watch.observe(slide));
    return () => watch.disconnect();
  }, [send, post.images.length]);
  // #endregion slides-seen

  const copy = () => {
    send('copy', '1');
    void navigator.clipboard
      ?.writeText(postAddress(post.slug))
      .then(() => setCopied(true))
      .catch(() => {});
  };

  return (
    <article
      ref={card}
      className={`${styles.card} op-glass op-tile`}
      aria-labelledby={`post-${post.slug}`}
      data-testid="blog-post"
    >
      <header className={styles.who}>
        <span className={styles.avatar} aria-hidden="true">
          {AUTHOR.initials}
        </span>
        <div>
          <p className={styles.name}>{AUTHOR.name}</p>
          <p className={styles.headline}>{AUTHOR.headline}</p>
          <p className={styles.when}>
            <time dateTime={post.published_at}>{postDate(post.published_at)}</time> · first posted
            on LinkedIn
          </p>
        </div>
      </header>

      <div
        className={open ? styles.body : `${styles.body} ${styles.clamped}`}
        data-testid="blog-post-body"
      >
        <h2 id={`post-${post.slug}`} className={styles.title}>
          {post.title}
        </h2>
        {bodyParts(post.body).map((part, index) =>
          part.tag ? (
            <span key={index} className={styles.hash}>
              {part.text}
            </span>
          ) : (
            <span key={index}>{part.text}</span>
          )
        )}
      </div>
      {!open && (
        <button
          type="button"
          className={styles.more}
          onClick={() => {
            setOpen(true);
            send('more', '1');
          }}
        >
          …see more
        </button>
      )}

      {post.images.length > 0 && (
        <section className={styles.slides} aria-label={`The slides (${post.images.length})`}>
          <h3 className={styles.slidesHead}>The slides ({post.images.length})</h3>
          {/* A strip wider than the card scrolls sideways, so it takes the keyboard's focus and names itself:
              a reader without a mouse can scroll it with the arrow keys (axe, scrollable-region-focusable). */}
          <ol
            ref={strip}
            className={styles.strip}
            tabIndex={0}
            aria-label={`The slides, ${post.images.length} of them, scroll sideways`}
            data-testid="blog-slides-strip"
          >
            {post.images.map((image) => (
              <li key={image.position} data-position={image.position} className={styles.slide}>
                <img
                  src={image.url}
                  alt={image.alt_text}
                  width={image.width}
                  height={image.height}
                  loading="lazy"
                  decoding="async"
                />
              </li>
            ))}
          </ol>
        </section>
      )}

      <p className={styles.stats} data-testid="blog-post-reads">
        <span>
          <strong>{post.reads}</strong> reads on TheYard
        </span>
        <span>{post.reads7d} this week</span>
      </p>

      <div className={styles.actions}>
        <a
          href={post.linkedin_url}
          target="_blank"
          rel="noreferrer"
          onClick={() => send('click', 'linkedin')}
        >
          Comment on LinkedIn
        </a>
        {post.code_url !== null && (
          <a
            href={post.code_url}
            target="_blank"
            rel="noreferrer"
            onClick={() => send('click', 'code')}
          >
            Read the code
          </a>
        )}
        <button type="button" onClick={copy}>
          {copied ? 'Link copied' : 'Copy link'}
        </button>
      </div>
    </article>
  );
}
