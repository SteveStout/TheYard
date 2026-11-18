/**
 * Does:      Draws a post's topics and keywords as small buttons, each filtering the blog by itself, and a run of text with
 *            a search's widened terms marked. A topic is never drawn with "#" (no hashtags on the blog).
 * Does not:  Know which filter is on (the page does) or what a term matches (the API does).
 * Used by:   BlogMini, BlogPostCard.
 */
import { marks, type FilterLabels } from '../../../lib/blogFilter';
import styles from './BlogTags.module.css';

/** The post's topics, then its keywords, each a button; nothing at all when there are no labels to draw them with. */
export function BlogTags({
  topics,
  keywords,
  labels,
  onTopic,
  onKeyword,
}: {
  topics: string[];
  keywords: string[];
  labels: FilterLabels | null;
  onTopic: (key: string) => void;
  onKeyword: (key: string) => void;
}) {
  if (labels === null || topics.length + keywords.length === 0) return null;
  return (
    <ul className={styles.tags} aria-label="Topics and keywords">
      {topics.map((key) => (
        <li key={`t-${key}`}>
          <button
            type="button"
            className={styles.tag}
            onClick={() => onTopic(key)}
            data-topic={key}
          >
            {labels.topics[key] ?? key}
          </button>
        </li>
      ))}
      {keywords.map((key) => (
        <li key={`k-${key}`}>
          <button
            type="button"
            className={`${styles.tag} ${styles.keyword}`}
            onClick={() => onKeyword(key)}
            data-keyword={key}
          >
            {labels.keywords[key] ?? key}
          </button>
        </li>
      ))}
    </ul>
  );
}

/** A run of text with each of a search's terms marked where it lands. */
export function Marked({ text, terms }: { text: string; terms: readonly string[] }) {
  return (
    <>
      {marks(text, terms).map((part, index) =>
        part.mark ? (
          <mark key={index} className={styles.mark}>
            {part.text}
          </mark>
        ) : (
          <span key={index}>{part.text}</span>
        )
      )}
    </>
  );
}
