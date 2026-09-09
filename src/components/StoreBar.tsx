import { useEffect, useState } from 'react';
import { fetchStores, note, segments, selectStore, type Stores } from '../lib/stores';
import styles from './StoreBar.module.css';

/**
 * The toggle at the top of the page (ADR: One container, both stores): which
 * store is serving this visit, and one click to the other. It reloads the
 * page after switching, because everything on it came from the store it is
 * leaving. Drawn only once the server has answered which stores there are,
 * so a container with one store shows the other family as not here rather
 * than as a button that does nothing.
 */
export function StoreBar() {
  const [stores, setStores] = useState<Stores | null>(null);
  const [switching, setSwitching] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void fetchStores(controller.signal).then((answer) => {
      if (answer) setStores(answer);
    });
    return () => controller.abort();
  }, []);

  if (stores === null) return null;

  const choose = async (key: string) => {
    setSwitching(key);
    setMessage(null);
    const result = await selectStore(key);
    if (!result.ok) {
      setSwitching(null);
      setMessage(result.message);
      return;
    }
    // The cookie is set; the page starts again on the other store.
    window.location.reload();
  };

  return (
    <div className={styles.bar} data-testid="store-bar">
      <div className={styles.inner}>
        <span className={styles.label} id="store-bar-label">
          Store
        </span>
        <div className={styles.toggle} role="radiogroup" aria-labelledby="store-bar-label">
          {segments(stores).map((segment) => (
            <button
              key={segment.key}
              type="button"
              role="radio"
              aria-checked={segment.current}
              className={styles.segment}
              data-store={segment.key}
              disabled={!segment.available && !segment.current}
              title={segment.title}
              onClick={() => {
                if (segment.available && switching === null) void choose(segment.key);
              }}
            >
              {switching === segment.key ? 'Switching…' : segment.label}
            </button>
          ))}
        </div>
        <span className={styles.note} data-testid="store-bar-note">
          {message ?? note(stores)}
        </span>
      </div>
    </div>
  );
}
