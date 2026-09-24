/**
 * The operator's desk: the key this browser holds, entered or forgotten.
 */
import { useState } from 'react';
import styles from '../AdminPanel.module.css';

/**
 * The operator's key on this browser (ADR: Site activity, and the line an
 * address does not cross, fifth addendum): typed into the page, because a
 * link that loses its query string on the way to a phone leaves this box as
 * the way in; remembered on this browser; forgotten on request for a shared
 * device. The cards behind the key (the visitor rows when this site serves
 * them, the reset links) show once it is known.
 */
export default function OperatorCard({
  adminKey,
  onEnterKey,
  onForget,
}: {
  adminKey: string | null;
  onEnterKey: (entered: string) => void;
  onForget: () => void;
}) {
  const [entered, setEntered] = useState('');
  return (
    <article className={`${styles.wide} op-glass`} data-testid="operator-card">
      <h2 className={styles.cardTitle}>Operator</h2>
      {adminKey === null ? (
        <>
          <p className={styles.muted}>
            The operator's cards answer only to the operator's key. Type it here once and this
            browser remembers it.
          </p>
          <form
            className={styles.filterRow}
            aria-label="Enter the operator's key"
            onSubmit={(event) => {
              event.preventDefault();
              onEnterKey(entered);
              setEntered('');
            }}
          >
            <label>
              Operator's key{' '}
              <input
                type="password"
                autoComplete="off"
                value={entered}
                onChange={(event) => setEntered(event.target.value)}
                data-testid="admin-key-entry"
              />
            </label>
            <button type="submit" className={styles.back} data-testid="admin-key-submit">
              Remember it on this browser
            </button>
          </form>
        </>
      ) : (
        <p className={styles.muted}>
          This browser remembers the key, so the operator's cards show without it in the address
          bar.{' '}
          <button
            type="button"
            className={styles.back}
            onClick={onForget}
            data-testid="admin-forget-key"
          >
            Forget the key on this browser
          </button>
        </p>
      )}
    </article>
  );
}
