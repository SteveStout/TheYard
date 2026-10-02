/**
 * The signed-in account view: who you are, a sign-out button, and the list of
 * vehicles you have bid on, each opening its page. Its own file because it is
 * one of the three things the account view can show, and the only one that
 * reads from the server on its own. The look is the account view's stylesheet.
 */
import { useEffect, useState } from 'react';
import { fetchHistory, logoutRequest, type Account, type HistoryEntry } from '../../../lib/auth';
import { formatCurrency, formatDate } from '../../../lib/format';
import styles from '../AccountPanel/AccountPanel.module.css';

/** What the signed-in view needs: the account, a way to hand the signed-out one back, and a way to open a vehicle. */
interface SignedInAccountProps {
  account: Account;
  onAccountChange: (account: Account) => void;
  onOpenVehicle: (vehicleId: string) => void;
}

// #region signed-in
/**
 * The signed-in view. The bid history is fetched again whenever the signed-in
 * email changes, and a failed fetch shows as an empty list rather than an error.
 */
export function SignedInAccount({ account, onAccountChange, onOpenVehicle }: SignedInAccountProps) {
  const [history, setHistory] = useState<HistoryEntry[] | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetchHistory(controller.signal)
      .then(setHistory)
      .catch(() => setHistory([]));
    return () => controller.abort();
  }, [account.email]);

  return (
    <div className={`${styles.panel} op-glass`}>
      <div className={styles.identity}>
        <div>
          <h2 className={styles.heading}>{account.email}</h2>
          {account.memberSinceMs !== null && (
            <p className={styles.lede}>Signed up {formatDate(account.memberSinceMs)}</p>
          )}
        </div>
        <button
          className={styles.secondary}
          type="button"
          onClick={() => {
            void logoutRequest().then(onAccountChange);
          }}
        >
          Sign out
        </button>
      </div>

      <h3 className={styles.subheading}>Your bids</h3>
      {history === null && <p className={styles.lede}>Loading.</p>}
      {history !== null && history.length === 0 && (
        <p className={styles.lede}>Nothing yet. Open a live auction and place one.</p>
      )}
      {history !== null && history.length > 0 && (
        <ul className={styles.history}>
          {history.map((entry) => (
            <li key={entry.vehicleId} className={styles.entry}>
              <button
                className={styles.entryButton}
                type="button"
                data-testid="history-entry"
                onClick={() => onOpenVehicle(entry.vehicleId)}
              >
                <span className={styles.entryTitle}>{entry.title}</span>
                <span className={styles.entryAmount}>{formatCurrency(entry.amount)}</span>
              </button>
              <span className={entry.outbid ? styles.outbid : styles.winning}>
                {entry.wonBuyNow
                  ? 'Bought'
                  : entry.outbid
                    ? `Outbid, now ${formatCurrency(entry.highestAmount)}`
                    : 'High bidder'}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
// #endregion signed-in
