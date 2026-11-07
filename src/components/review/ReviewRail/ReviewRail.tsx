/**
 * The review route's rail (ADR: Ten minutes, seven stops, the addendum on the rail): beside the stops at a wide
 * desk, the person the route is about with his links, then every recommendation's pull quote with a link to its
 * card on the recommendations page; under that width the links fold under the lede and the quotes after the
 * finish. The quotes are read from /api/recommendations, the same answer the landing column reads, and the page
 * keeps drawing whole on the server: until the answer arrives the rail holds its links and no quotes.
 */
import { useEffect, useState } from 'react';
import {
  RECOMMENDATIONS_URL,
  recommendationsFrom,
  type Recommendation,
} from '../../../lib/recommendations';
import { cardAddress } from '../../../lib/recommendationsPage';
import { request } from '../../../lib/request';
import pills from '../ReviewPage/ReviewPage.module.css';
import styles from './ReviewRail.module.css';

/** The person's links: each the site's own or his own public page. */
const LINKS = [
  { label: 'The resume', href: '/resume?pdf=1' },
  { label: 'About Steven', href: '/about' },
  { label: 'The recommendations', href: '/?view=recommendations' },
  { label: 'LinkedIn', href: 'https://www.linkedin.com/in/stevenwstout/' },
  { label: 'GitHub', href: 'https://github.com/SteveStout/TheYard' },
];

export function ReviewRail() {
  const [quotes, setQuotes] = useState<Recommendation[] | null>(null);
  useEffect(() => {
    let live = true;
    request(RECOMMENDATIONS_URL, undefined, { background: true })
      .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
      .catch(() => null)
      .then((answer) => {
        if (live) setQuotes(recommendationsFrom(answer));
      });
    return () => {
      live = false;
    };
  }, []);

  return (
    <div className={styles.rail} data-testid="review-rail">
      <section className={`${styles.person} op-glass`} aria-labelledby="review-rail-person">
        <h2 id="review-rail-person" className={styles.heading}>
          Steven Stout
        </h2>
        <p className={styles.line}>
          Lead / Staff .NET engineer. The work below is his, running live.
        </p>
        <ul className={styles.links}>
          {LINKS.map((link) => (
            <li key={link.href}>
              <a className={pills.secondary} href={link.href}>
                {link.label}
              </a>
            </li>
          ))}
        </ul>
      </section>
      {quotes && quotes.length > 0 && (
        <section
          className={styles.quotes}
          aria-labelledby="review-rail-quotes"
          data-testid="review-quotes"
        >
          <h2 id="review-rail-quotes" className={styles.heading}>
            Who worked with him says
          </h2>
          <ul className={styles.list}>
            {quotes.map((entry) => (
              <li key={entry.name} className={`${styles.quote} op-glass`}>
                <blockquote className={styles.words}>
                  <p>{entry.pull}</p>
                </blockquote>
                <p className={styles.who}>
                  {entry.name}, {entry.title}
                </p>
                <a className={styles.more} href={cardAddress(entry.name)}>
                  Read the whole thing
                  <span className={styles.srOnly}>, {entry.name}</span>
                </a>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
