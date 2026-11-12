/**
 * Does:      Draws the hook, the first box on each page a reader is sent to (ADR-132, A hook on every page): a title when
 *            the page has one, the page's words from data/hooks.json or its own (a post's summary), one figure, and one
 *            link. The landing draws its sentence as the page's heading.
 * Does not:  Read anything: every word and figure is handed in, so the box draws on the server with no read and the
 *            browser's first draw matches it.
 * Used by:   Landing.tsx, BlogPage.tsx.
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
}) {
  const [first = '', ...rest] = lines;
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
