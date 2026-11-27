/**
 * Does:      Draws the hook, the first box on each page a reader is sent to (ADR-132, A hook on every page): a title when
 *            the page has one, the page's words from data/hooks.json or its own (a post's summary), one figure, and one
 *            link. The landing draws its sentence as the page's heading, in the split layout only it asks for (ADR-132,
 *            the addendum on the split hook): the words on the left with a name line above the heading, one sentence
 *            under it and the page's moves under the hook's sentences; the figure, its link and one more piece on the
 *            right; one column under 1280; on the home page it stands on the ground with no frame (ADR-142).
 * Does not:  Read anything: every word and figure is handed in, so the box draws on the server with no read and the
 *            browser's first draw matches it.
 * Used by:   Landing.tsx, StackPage.tsx.
 */
import type { MouseEvent, ReactNode } from 'react';
import styles from './Hook.module.css';

export function Hook({
  page,
  title,
  lines,
  headingId,
  headingClass,
  caption,
  figure,
  link,
  onLink,
  split,
  ground = false,
}: {
  /** The page's name in data/hooks.json, which the box carries as data-hook. */
  page: string;
  /** A title above the words, a post's, drawn as text so the page keeps its own heading. */
  title?: string;
  lines: string[];
  /** When given, the first line is the page's h1 under this id, styled by the page's own title class. */
  headingId?: string;
  headingClass?: string;
  /** What the figure shows, in a line above it. */
  caption?: string;
  figure?: ReactNode;
  link?: { label: string; href: string } | null;
  /** Opens the link inside the app; the address stays a real one for a crawler and a new tab. */
  onLink?: (event: MouseEvent<HTMLAnchorElement>) => void;
  /** The landing's two columns: what stands above the heading, the sentence under it, the moves and the piece under the link. */
  split?: { above: ReactNode; lede: string; moves: ReactNode; after: ReactNode };
  /** On the ground like the review route's head, with no glass frame of its own (the home page, ADR-142). */
  ground?: boolean;
}) {
  const [first = '', ...rest] = lines;
  if (split) {
    return (
      <section
        className={`${styles.hook} ${styles.split} ${ground ? styles.ground : 'op-glass'}`}
        data-testid="hook"
        data-hook={page}
      >
        <div className={styles.hero}>
          <div className={styles.words}>
            {split.above}
            <h1 id={headingId} className={headingClass ?? styles.sentence}>
              {first}
            </h1>
            <p className={styles.lede}>{split.lede}</p>
            {rest.map((line) => (
              <p key={line} className={styles.sentence}>
                {line}
              </p>
            ))}
            {split.moves}
          </div>
          <div className={styles.figcol}>
            {figure && (
              <div className={styles.figure}>
                {caption && <p className={styles.caption}>{caption}</p>}
                {figure}
              </div>
            )}
            {link && (
              <a className={styles.link} href={link.href} onClick={onLink}>
                {link.label}
              </a>
            )}
            {split.after}
          </div>
        </div>
      </section>
    );
  }
  return (
    <section className={`${styles.hook} op-glass`} data-testid="hook" data-hook={page}>
      {title && <p className={styles.title}>{title}</p>}
      {headingId ? (
        <h1 id={headingId} className={headingClass ?? styles.sentence}>
          {first}
        </h1>
      ) : (
        first && <p className={styles.sentence}>{first}</p>
      )}
      {rest.map((line) => (
        <p key={line} className={styles.sentence}>
          {line}
        </p>
      ))}
      {figure && (
        <div className={styles.figure}>
          {caption && <p className={styles.caption}>{caption}</p>}
          {figure}
        </div>
      )}
      {link && (
        <a className={styles.link} href={link.href} onClick={onLink}>
          {link.label}
        </a>
      )}
    </section>
  );
}
