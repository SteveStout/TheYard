import { useEffect, useState } from 'react';
import {
  fetchHistory,
  loginRequest,
  logoutRequest,
  registerRequest,
  type Account,
  type HistoryEntry,
} from '../lib/auth';
import { formatCurrency } from '../lib/format';
import styles from './AccountPanel.module.css';

interface AccountPanelProps {
  account: Account;
  onAccountChange: (account: Account) => void;
  onOpenVehicle: (vehicleId: string) => void;
  onBack: () => void;
}

/**
 * The account view (ADR: Accounts and per-user bids), at ?view=account.
 *
 * Signed out it is one form that does both jobs, because register and sign in
 * ask for the same two things and a demo that makes you choose a tab first is
 * asking you to read before you can type.
 */
export function AccountPanel({
  account,
  onAccountChange,
  onOpenVehicle,
  onBack,
}: AccountPanelProps) {
  return (
    <section className={styles.wrap} aria-label="Account">
      {/* The same head as the Admin tab, so a view switch looks like a view
          switch. The h1 belongs here rather than inside either branch: signing
          in changes what the page offers, not which page you are on. */}
      <div className={styles.head}>
        <h1 className={styles.title}>Account</h1>
        <button type="button" className={styles.back} onClick={onBack}>
          Back to inventory
        </button>
      </div>
      {account.signedIn ? (
        <SignedIn
          account={account}
          onAccountChange={onAccountChange}
          onOpenVehicle={onOpenVehicle}
        />
      ) : (
        <SignInForm onAccountChange={onAccountChange} />
      )}
    </section>
  );
}

// #region sign-in
function SignInForm({ onAccountChange }: { onAccountChange: (account: Account) => void }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function attempt(mode: 'register' | 'login') {
    setBusy(true);
    setMessage(null);
    const result = await (mode === 'register'
      ? registerRequest(email, password)
      : loginRequest(email, password));
    setBusy(false);
    if (result.ok) {
      onAccountChange(result.account);
      return;
    }
    setMessage(result.message);
  }

  return (
    <div className={styles.panel}>
      <h2 className={styles.heading}>Sign in to bid</h2>
      <p className={styles.lede}>
        Bids belong to an account, so the auction can tell two people apart. Nothing is emailed and
        nothing is shared; this is a demo, and the address is only the name your bids are under.
      </p>

      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault();
          void attempt('login');
        }}
      >
        <label className={styles.field}>
          <span className={styles.label}>Email</span>
          <input
            className={styles.input}
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </label>
        <label className={styles.field}>
          <span className={styles.label}>Password</span>
          <input
            className={styles.input}
            type="password"
            autoComplete="current-password"
            required
            minLength={8}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
          <span className={styles.hint}>Eight characters or more.</span>
        </label>

        {message && (
          <p className={styles.error} role="alert">
            {message}
          </p>
        )}

        <div className={styles.actions}>
          <button className={styles.primary} type="submit" disabled={busy}>
            Sign in
          </button>
          <button
            className={styles.secondary}
            type="button"
            disabled={busy}
            onClick={() => void attempt('register')}
          >
            Create an account
          </button>
        </div>
      </form>
    </div>
  );
}
// #endregion sign-in

// #region signed-in
function SignedIn({
  account,
  onAccountChange,
  onOpenVehicle,
}: {
  account: Account;
  onAccountChange: (account: Account) => void;
  onOpenVehicle: (vehicleId: string) => void;
}) {
  const [history, setHistory] = useState<HistoryEntry[] | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    fetchHistory(controller.signal)
      .then(setHistory)
      .catch(() => setHistory([]));
    return () => controller.abort();
  }, [account.email]);

  return (
    <div className={styles.panel}>
      <div className={styles.identity}>
        <div>
          <h2 className={styles.heading}>{account.email}</h2>
          {account.memberSinceMs !== null && (
            <p className={styles.lede}>
              Signed up {new Date(account.memberSinceMs).toLocaleDateString()}
            </p>
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
