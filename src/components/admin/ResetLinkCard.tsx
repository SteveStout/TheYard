/**
 * Reset a password: a link minted behind the operator's key (ADR: Reset is one person's).
 */
import { useState } from 'react';
import styles from '../AdminPanel.module.css';
import { About } from './common';

/**
 * A password reset link, minted by the operator for one account on this
 * site's store (ADR: Accounts and per-user bids, addendum). Behind the key,
 * because minting a link for somebody else's account is exactly what a
 * stranger must not be able to do. The operator hands the link to the person
 * by whatever means they have; the link works once, for an hour.
 */
export default function ResetLinkCard({ adminKey }: { adminKey: string | null }) {
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [made, setMade] = useState<{ email: string; url: string; expires_at: string } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  if (adminKey === null) return null;

  return (
    <article className={`${styles.wide} op-glass`} data-testid="reset-link-card">
      <h2 className={styles.cardTitle}>Reset a password</h2>
      <About>
        Mint a reset link for an account on this site's store and hand it to the person. The link
        works once, for an hour, and dies the moment the password changes. An emailed link is the
        same mechanism with a sender in front of it.
      </About>
      <form
        className={styles.filterRow}
        aria-label="Mint a reset link"
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setMessage(null);
          setMade(null);
          void fetch('/api/admin/reset-links', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Admin-Key': adminKey },
            body: JSON.stringify({ email }),
          })
            .then(async (r) => {
              if (r.ok) {
                setMade((await r.json()) as { email: string; url: string; expires_at: string });
                return;
              }
              const body = (await r.json().catch(() => ({}))) as { detail?: string };
              setMessage(body.detail ?? `The link was not made (${r.status}).`);
            })
            .catch(() => setMessage('The server could not be reached.'))
            .finally(() => setBusy(false));
        }}
      >
        <label>
          Email{' '}
          <input
            type="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            data-testid="reset-link-email"
          />
        </label>
        <button
          type="submit"
          className={styles.back}
          disabled={busy}
          data-testid="reset-link-submit"
        >
          Mint a link
        </button>
      </form>
      {message && (
        <p className={styles.muted} role="alert" data-testid="reset-link-refused">
          {message}
        </p>
      )}
      {made && (
        <p className={styles.muted} data-testid="reset-link-made">
          For {made.email}, until {new Date(made.expires_at).toLocaleTimeString()}:{' '}
          <code className={styles.mono} data-testid="reset-link-url">
            {made.url}
          </code>
        </p>
      )}
    </article>
  );
}
