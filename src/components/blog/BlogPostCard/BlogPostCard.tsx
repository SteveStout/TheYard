/**
 * Does:      Draws one post in full, in the feed layout readers know from LinkedIn and in the site's own colours: who wrote
 *            it, when and how long it takes, the title, where a search matched, "From the work" when a review route case
 *            card names the post, its topics and keywords, the body drawn
 *            on save and clamped with "see more", the slides as a strip of thumbnails that open the slide viewer, the
 *            reads, and three actions (comment on LinkedIn, read the code, copy the link). Reports the read and what the
 *            reader does, a link in the body as a click that names its host.
 * Does not:  Fetch the post (BlogPage hands it over), draw the body's Markdown (the API drew it on save) or keep anything
 *            about the reader (useBlogReport.ts sends, the API keeps).
 * Used by:   BlogPage.tsx.
 */
import { useEffect, useRef, useState } from 'react';
import { postDate, type BlogPost } from '../../../lib/blog';
import { matchLine, minutesLine, type FilterLabels } from '../../../lib/blogFilter';
import { useBlogReport } from '../../../hooks/useBlogReport';
import { fromTheWork } from '../../../lib/reviewCases';
import { BlogTags, Marked } from '../BlogTags';
import { SlideViewer } from '../SlideViewer';
import styles from './BlogPostCard.module.css';

/** The author's line, the resume's own words. */
const AUTHOR = {
  initials: 'SS',
  name: 'Steven Stout',
  headline:
    'Lead / Staff .NET Engineer | 12 Years Full Stack .NET, 7 Fully Remote | Cloud Platform Architecture',
};

/** The strip's thumbnail width in CSS pixels; the 320 px thumbnail covers it twice over. */
const THUMB_WIDTH = 150;

/** The address a copied link carries: the post on the blog, with no tagged code, and a slide when one is named. */
export function postAddress(slug: string, slide?: number): string {
  const origin = typeof window === 'undefined' ? '' : window.location.origin;
  return `${origin}/?view=blog&post=${slug}${slide ? `&slide=${slide}` : ''}`;
}

export function BlogPostCard({
  post,
  open = false,
  matched = [],
  terms = [],
  labels = null,
  slide = null,
  onSlide = () => {},
  onTopic = () => {},
  onKeyword = () => {},
}: {
  post: BlogPost;
  open?: boolean;
  matched?: string[];
  terms?: readonly string[];
  labels?: FilterLabels | null;
  slide?: number | null;
  onSlide?: (position: number | null) => void;
  onTopic?: (key: string) => void;
  onKeyword?: (key: string) => void;
}) {
  const card = useRef<HTMLElement>(null);
  const strip = useRef<HTMLOListElement>(null);
  const send = useBlogReport(post.slug, card);
  const [unclamped, setUnclamped] = useState(open);
  const [copied, setCopied] = useState(false);
  const viewing = slide === null ? -1 : post.images.findIndex((image) => image.position === slide);
  const line = matchLine(matched);
  // A post a case card names leads back to that card (reviewCases.ts); none does yet, so nothing is drawn today.
  const work = fromTheWork(post.slug, '');

  // #region slides-seen
  // Each slide counts once, when most of it has been in the strip's view, or when the viewer shows it.
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
    list.querySelectorAll('li').forEach((each) => watch.observe(each));
    return () => watch.disconnect();
  }, [send, post.images.length]);
  useEffect(() => {
    if (viewing >= 0) send('slide', String(post.images[viewing].position));
  }, [send, viewing, post.images]);
  // #endregion slides-seen

  const openSlides = (position: number) => {
    send('click', 'slides');
    onSlide(position);
  };

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
            on LinkedIn · {minutesLine(post.reading_minutes)}
          </p>
        </div>
      </header>

      <h2 id={`post-${post.slug}`} className={styles.title}>
        <Marked text={post.title} terms={terms} />
      </h2>
      {line && (
        <p className={styles.why} data-testid="blog-matched">
          {line}
        </p>
      )}
      {work && (
        <p className={styles.why} data-testid="blog-from-work">
          <a href={work.href}>{work.line}</a>
        </p>
      )}
      <BlogTags
        topics={post.topics}
        keywords={post.keywords}
        labels={labels}
        onTopic={onTopic}
        onKeyword={onKeyword}
      />

      {/* The body was drawn to HTML on save from the post's own Markdown, every piece of its text escaped and only an
          http or https address let through as a link (BlogBodyHtml); a click on a link is a way out of the post. */}
      <div
        className={unclamped ? styles.body : `${styles.body} ${styles.clamped}`}
        data-testid="blog-post-body"
        onClick={(event) => {
          const link = (event.target as HTMLElement).closest('a');
          if (!link) return;
          try {
            send('click', `body:${new URL(link.href).host}`);
          } catch {
            // Not an address the browser can read: the link still opens, and nothing is kept about it.
          }
        }}
        dangerouslySetInnerHTML={{ __html: post.body_html }}
      />
      {!unclamped && (
        <button
          type="button"
          className={styles.more}
          onClick={() => {
            setUnclamped(true);
            send('more', '1');
          }}
        >
          …see more
        </button>
      )}

      {post.images.length > 0 && (
        <section className={styles.slides} aria-label={`The slides (${post.images.length})`}>
          <div className={styles.slidesHead}>
            <h3>The slides ({post.images.length})</h3>
            <button
              type="button"
              className={styles.viewAll}
              onClick={() => openSlides(post.images[0].position)}
              data-testid="slides-view-all"
            >
              View all slides
            </button>
          </div>
          {/* A strip wider than the card scrolls sideways, so it takes the keyboard's focus and names itself:
              a reader without a mouse can scroll it with the arrow keys (axe, scrollable-region-focusable). */}
          <ol
            ref={strip}
            className={styles.strip}
            tabIndex={0}
            aria-label={`The slides, ${post.images.length} of them, scroll sideways`}
            data-testid="blog-slides-strip"
          >
            {post.images.map((image, index) => (
              <li key={image.position} data-position={image.position} className={styles.slide}>
                <button
                  type="button"
                  className={styles.thumb}
                  onClick={() => openSlides(image.position)}
                  aria-label={`Open slide ${index + 1} of ${post.images.length}: ${image.alt_text}`}
                >
                  <img
                    src={image.thumb_url}
                    alt=""
                    width={THUMB_WIDTH}
                    height={Math.round((THUMB_WIDTH * image.height) / Math.max(image.width, 1))}
                    loading={index === 0 ? 'eager' : 'lazy'}
                    fetchPriority={index === 0 ? 'high' : 'auto'}
                    decoding="async"
                  />
                  <span className={styles.number} aria-hidden="true">
                    {index + 1}
                  </span>
                </button>
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

      {viewing >= 0 && (
        <SlideViewer
          title={post.title}
          images={post.images}
          at={viewing}
          slideAddress={(position) => postAddress(post.slug, position)}
          onMove={(index) => onSlide(post.images[index].position)}
          onClose={() => onSlide(null)}
        />
      )}
    </article>
  );
}
