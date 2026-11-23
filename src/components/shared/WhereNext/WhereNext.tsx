/**
 * Does:      Draws a page's "Where next" footer (ladder v7, page flow): at most two links from data/hooks.json, each
 *            with its one line of reason and its minutes, the same list the API draws on /about and the resume door.
 *            Its hooks for the picture checks (data-mock where-next and next-link) are on every page that draws it.
 * Does not:  Choose the links (the file does), type a count, or link to Admin (pageHooks.test.ts holds the file to that).
 * Used by:   StackPage.tsx, BlogPage.tsx, DocDialog.tsx.
 */
import { useSyncExternalStore } from 'react';
import { holdCode, sessionOrNull, withCode } from '../../../lib/links';
import { nextFor, type NextPage } from '../../../lib/pageHooks';
import styles from './WhereNext.module.css';

/** The held code never changes during a visit, so there is nothing to subscribe to. */
const noChange = () => () => {};
const heldCode = () => holdCode('', sessionOrNull());
const noCode = () => null;

/** The page's footer, or nothing when the file has no links for it. */
export function WhereNext({ page }: { page: NextPage }) {
  const links = nextFor(page);
  // The visit's code, put on the links once the page is live: the landing is drawn at the build with none, so the
  // hydrating render reads the server's answer (none) and the next one reads the session (ladder v7b).
  const code = useSyncExternalStore(noChange, heldCode, noCode);
  if (links.length === 0) return null;
  return (
    <nav
      className={`${styles.next} op-glass`}
      aria-label="Where next"
      data-testid="where-next"
      data-mock="where-next"
      data-next={page}
    >
      <p className={styles.title}>Where next</p>
      <ul className={styles.links}>
        {links.map((link) => (
          <li key={link.href}>
            <a href={withCode(link.href, code)} data-mock="next-link">
              {link.label}
            </a>
            <span className={styles.reason}>
              {link.reason} <span className={styles.minutes}>{link.minutes} min</span>
            </span>
          </li>
        ))}
      </ul>
    </nav>
  );
}
