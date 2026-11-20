/**
 * Does:      Draws a post's title as a link to the post's own address (ADR: Search engines are told about every post), so
 *            the HTML a crawler reads carries every post: the heading around it keeps its look, and a plain click opens the
 *            post in place through onOpen, or does nothing where the reader is already on it; a modified click, or a
 *            plain one with neither, keeps the browser's own meaning and loads the address.
 * Does not:  Draw the heading (the card does, with its level and id) or open anything itself (the page's onOpen does).
 * Used by:   BlogMini.tsx, BlogPostCard.tsx.
 */
import type { ReactNode } from 'react';
import { postLink } from '../../../lib/blog';
import { plainClick } from '../../../lib/sectionScroll';
import styles from './PostTitleLink.module.css';

export function PostTitleLink({
  slug,
  onOpen,
  here = false,
  children,
}: {
  slug: string;
  onOpen?: (slug: string) => void;
  here?: boolean;
  children: ReactNode;
}) {
  return (
    <a
      className={styles.link}
      href={postLink(slug)}
      onClick={(event) => {
        if (!plainClick(event) || (!onOpen && !here)) return;
        event.preventDefault();
        onOpen?.(slug);
      }}
      data-mock="post-title-link"
    >
      {children}
    </a>
  );
}
