import { useEffect, useState } from 'react';
import { fetchStores, note, segments, type Stores } from '../lib/stores';
import styles from './StoreBar.module.css';

/**
 * The bar at the top of the page (ADR: One container, both stores, and its
 * addendum on the toggle moving to the sites): which site this is, and one
 * click to the other. The other segment is a link to the other site at this
 * same path and query, so the address bar changes and the page that arrives
 * is that site's own. Drawn only once the server has answered, so a
 * container that names no other site shows the other segment as not here
 * rather than as a control that does nothing.
 */
export function StoreBar() {
  const [stores, setStores] = useState<Stores | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetchStores(controller.signal).then((answer) => {
      if (answer) setStores(answer);
    });
    return () => controller.abort();
  }, []);

  if (stores === null) return null;

  const here = { pathname: window.location.pathname, search: window.location.search };

  return (
    <div className={styles.bar} data-testid="store-bar">
      <div className={styles.inner}>
        <span className={styles.label} id="store-bar-label">
          Store
        </span>
        <nav className={styles.toggle} aria-labelledby="store-bar-label">
          {segments(stores, here).map((segment) =>
            segment.current ? (
              <span
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                aria-current="page"
                title={segment.title}
              >
                {segment.label}
              </span>
            ) : segment.href !== null ? (
              <a
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                href={segment.href}
                title={segment.title}
              >
                {segment.label}
              </a>
            ) : (
              <span
                key={segment.key}
                className={styles.segment}
                data-store={segment.key}
                role="link"
                aria-disabled="true"
                title={segment.title}
              >
                {segment.label}
              </span>
            )
          )}
        </nav>
        <span className={styles.note} data-testid="store-bar-note">
          {note(stores)}
        </span>
      </div>
    </div>
  );
}
