import { useEffect, useState } from 'react';
import { fetchStores, note, segments, type Stores } from '../lib/stores';
import { Ring } from './Ring';
import styles from './StoreBar.module.css';

/**
 * The bar at the top of the page (ADR: One container, both stores, and its
 * addendum on the toggle moving to the sites): which site this is, and one
 * click to the other. The other segment is a link to the other site at this
 * same path and query, so the address bar changes and the page that arrives
 * is that site's own. The band is drawn from the first paint at its full
 * height, so the page under it does not move when the answer arrives
 * (measured at 1280 on 1.0.3.4: the whole page shifted 83 px, a layout shift
 * of 0.12, when this bar appeared); the segments are drawn only once the
 * server has answered, so a container that names no other site shows the
 * other segment as not here rather than as a control that does nothing.
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

  const here = { pathname: window.location.pathname, search: window.location.search };

  return (
    <div
      className={styles.bar}
      data-testid="store-bar"
      data-state={stores === null ? 'loading' : 'ready'}
    >
      <div className={styles.inner}>
        <span className={styles.label} id="store-bar-label">
          Store
        </span>
        <nav
          className={`${styles.toggle} op-seg`}
          aria-labelledby="store-bar-label"
          aria-busy={stores === null}
        >
          {(stores === null ? [] : segments(stores, here)).map((segment) =>
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
        {/* The stores that came up, of those this container runs (the
            operator's look): an empty track until the answer, at its size from
            the first paint, so the band does not move when it fills. */}
        <span className={styles.ready}>
          <Ring
            value={stores === null ? 0 : stores.stores.filter((store) => store.ready).length}
            max={stores === null ? 1 : stores.stores.length}
            size="tiny"
            label={
              stores === null
                ? null
                : `${stores.stores.filter((store) => store.ready).length} of ${stores.stores.length} stores ready`
            }
            testId="store-bar-ready"
          />
        </span>
        <span
          className={styles.note}
          data-testid="store-bar-note"
          title={stores === null ? undefined : note(stores)}
        >
          {stores === null ? '' : note(stores)}
        </span>
      </div>
    </div>
  );
}
