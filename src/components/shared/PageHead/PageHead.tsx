/**
 * Does:      Draws the head every landing view opens with, the review route's hero grid made shared (ADR-142, the addendum
 *            on the landing views): a main column and the 344 px side, written with three areas (head, body, side) so a
 *            page says where its side goes on a phone, right after the title ("early", About's photo) or after the body
 *            ("late", the blog's side cards). The head is the eyebrow, the one h1 with the gold rule, and the lede.
 * Does not:  Hold a word of its own (each page hands in its eyebrow, title, lede, body and side), draw a card's glass
 *            (panels.css does) or the title's and eyebrow's look (sales.css, op-title and op-eyebrow, does).
 * Used by:   ReviewVerdict.tsx, AboutPage.tsx, BlogPage.tsx.
 */
import type { ReactNode, Ref } from 'react';
import { Eyebrow } from '../Eyebrow';
import styles from './PageHead.module.css';

export function PageHead({
  eyebrow,
  title,
  titleId,
  titleClass = '',
  lede,
  side,
  sideLabel,
  sideMock = 'page-side',
  sideClass = '',
  sideOnPhone = 'late',
  bodyClass = '',
  holder,
  testId,
  children,
}: {
  /** The eyebrow's parts, "Steven Stout" first. */
  eyebrow: readonly string[];
  /** The page's one h1. */
  title: ReactNode;
  titleId: string;
  /** A page's own touch on the title (the review route's title fits its words). */
  titleClass?: string;
  /** The one sentence under the title, when the page has one. */
  lede?: ReactNode;
  /** What stands in the 344 px column. */
  side: ReactNode;
  /** When given, the side is an aside a screen reader names by these words. */
  sideLabel?: string;
  /** The side's data-mock name, which the design checks read. */
  sideMock?: string;
  /** A page's own layout for its side's cards (the review route sets its two side by side under 1280). */
  sideClass?: string;
  /** Where the side goes once the columns stack: straight after the title, or after the body. */
  sideOnPhone?: 'early' | 'late';
  /** A page's own layout for its body (the review route moves its buttons up on a phone). */
  bodyClass?: string;
  /** The ref a page's motions watch the head through. */
  holder?: Ref<HTMLDivElement>;
  testId?: string;
  /** The body: what the main column holds under the head. */
  children: ReactNode;
}) {
  const Side = sideLabel === undefined ? 'div' : 'aside';
  return (
    <div
      ref={holder}
      className={styles.grid}
      data-side={sideOnPhone}
      data-mock="page-head"
      data-testid={testId}
    >
      <div className={styles.head}>
        <Eyebrow parts={eyebrow} />
        <h1 id={titleId} className={`op-title ${titleClass}`} data-mock="title">
          {title}
        </h1>
        {lede !== undefined && (
          <p className={styles.lede} data-mock="lede">
            {lede}
          </p>
        )}
      </div>
      <div className={`${styles.body} ${bodyClass}`}>{children}</div>
      <Side className={`${styles.side} ${sideClass}`} aria-label={sideLabel} data-mock={sideMock}>
        {side}
      </Side>
    </div>
  );
}
