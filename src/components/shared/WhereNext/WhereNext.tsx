/**
 * Does:      Draws a page's "Where next" footer (ladder v7, page flow): at most two links from data/hooks.json, each
 *            with its one line of reason and its minutes, the same list the API draws on /about and the resume door.
 * Does not:  Choose the links (the file does), type a count, or link to Admin (pageHooks.test.ts holds the file to that).
 * Used by:   Landing.tsx, ReviewPage.tsx, BlogPage.tsx, DocDialog.tsx.
 */
import { nextFor, type NextPage } from '../../../lib/pageHooks';
import styles from './WhereNext.module.css';

/** The page's footer, or nothing when the file has no links for it. */
export function WhereNext({ page }: { page: NextPage }) {
  const links = nextFor(page);
  if (links.length === 0) return null;
  return (
    <nav
      className={`${styles.next} op-glass`}
      aria-label="Where next"
      data-testid="where-next"
      data-next={page}
    >
      <p className={styles.title}>Where next</p>
      <ul className={styles.links}>
        {links.map((link) => (
          <li key={link.href}>
            <a href={link.href}>{link.label}</a>
            <span className={styles.reason}>
              {link.reason} <span className={styles.minutes}>{link.minutes} min</span>
            </span>
          </li>
        ))}
      </ul>
    </nav>
  );
}
