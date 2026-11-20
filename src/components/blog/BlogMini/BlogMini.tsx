/**
 * Does:      Draws the short card a post gets in the list, among the related posts and in the home page's band: its cover
 *            slide's thumbnail (which opens the post at its first slide), its title as a link to the post's own address
 *            (a plain click opens it in place, a modified click keeps the browser's meaning) and its summary with a search's
 *            terms marked, where a search matched, its topics and keywords, and its date, reading time and reads.
 * Does not:  Fetch anything (the card is handed over) or draw the post in full (BlogPostCard does).
 * Used by:   BlogPage.tsx, BlogPostNav.tsx, LatestPosts.tsx.
 */
import { postDate, type BlogCard } from '../../../lib/blog';
import { matchLine, minutesLine, type FilterLabels } from '../../../lib/blogFilter';
import { BlogTags, Marked } from '../BlogTags';
import { PostTitleLink } from '../PostTitleLink';
import styles from './BlogMini.module.css';

/** The thumbnail's drawn width: the 320 px thumbnail is twice the cover's width on a phone. */
const COVER_WIDTH = 96;

export function BlogMini({
  post,
  onOpen,
  onOpenSlides,
  terms = [],
  labels = null,
  onTopic = () => {},
  onKeyword = () => {},
}: {
  post: BlogCard;
  onOpen: (slug: string) => void;
  onOpenSlides?: (slug: string) => void;
  terms?: readonly string[];
  labels?: FilterLabels | null;
  onTopic?: (key: string) => void;
  onKeyword?: (key: string) => void;
}) {
  const cover = post.cover;
  const matched = matchLine(post.matched);
  return (
    <article
      className={`${styles.mini} ${cover ? styles.withCover : ''} op-glass op-tile`}
      data-testid="blog-mini"
    >
      {cover && (
        <button
          type="button"
          className={styles.cover}
          onClick={() => (onOpenSlides ?? onOpen)(post.slug)}
          aria-label={`Open the slides of ${post.title}`}
        >
          <img
            src={cover.thumb_url}
            alt=""
            width={COVER_WIDTH}
            height={Math.round((COVER_WIDTH * cover.height) / Math.max(cover.width, 1))}
            loading="lazy"
            decoding="async"
          />
          <span aria-hidden="true">{post.slides} slides</span>
        </button>
      )}
      <div className={styles.text}>
        <h3 className={styles.title}>
          <PostTitleLink slug={post.slug} onOpen={onOpen}>
            <Marked text={post.title} terms={terms} />
          </PostTitleLink>
        </h3>
        <p className={styles.summary}>
          <Marked text={post.summary} terms={terms} />
        </p>
        {matched && (
          <p className={styles.why} data-testid="blog-matched">
            {matched}
          </p>
        )}
        <BlogTags
          topics={post.topics}
          keywords={post.keywords}
          labels={labels}
          onTopic={onTopic}
          onKeyword={onKeyword}
        />
        <p className={styles.foot}>
          <time dateTime={post.published_at}>{postDate(post.published_at)}</time>
          <span>{minutesLine(post.reading_minutes)}</span>
          <span>{post.reads} reads</span>
          <button type="button" className={styles.read} onClick={() => onOpen(post.slug)}>
            Read the post <span aria-hidden="true">→</span>
          </button>
        </p>
      </div>
    </article>
  );
}
