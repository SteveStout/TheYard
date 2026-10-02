/**
 * The second half of a password reset: the page was opened from a reset link,
 * so it asks only for the new password. Its own file because it is one of the
 * three things the account view can show, with its own state. The look is the
 * account view's stylesheet.
 */
import { useState } from 'react';
import { resetRequest, type Account } from '../../../lib/auth';
import styles from '../AccountPanel/AccountPanel.module.css';

/** What the reset form needs: the link's token, and a way to hand the signed-in account back up. */
interface ResetPasswordFormProps {
  token: string;
  onAccountChange: (account: Account) => void;
}

// #region reset
/**
 * The second half of a password reset (ADR: Accounts and per-user bids,
 * addendum): the link carried a token, the visitor chooses a new password,
 * and the server signs them in. One field, because the link already said
 * who they are; a wrong or spent link is one sentence from the server.
 */
export function ResetPasswordForm({ token, onAccountChange }: ResetPasswordFormProps) {
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  return (
    <div className={`${styles.panel} op-glass`}>
      <h2 className={styles.heading}>Choose a new password</h2>
      <p className={styles.lede}>
        This link was made for your account and works once, for an hour. Choose a new password and
        you are signed in.
      </p>
      <form
        className={styles.form}
        onSubmit={(event) => {
          event.preventDefault();
          setBusy(true);
          setMessage(null);
          void resetRequest(token, password).then((result) => {
            setBusy(false);
            if (result.ok) {
              onAccountChange(result.account);
              return;
            }
            setMessage(result.message);
          });
        }}
      >
        <label className={styles.field}>
          <span className={styles.label}>New password</span>
          <input
            className={styles.input}
            type="password"
            autoComplete="new-password"
            required
            minLength={8}
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            data-testid="reset-password"
          />
          <span className={styles.hint}>Eight characters or more.</span>
        </label>
        {message && (
          <p className={styles.error} role="alert">
            {message}
          </p>
        )}
        <div className={styles.actions}>
          <button
            className={styles.primary}
            type="submit"
            disabled={busy}
            data-testid="reset-submit"
          >
            Set the password and sign in
          </button>
        </div>
      </form>
    </div>
  );
}
// #endregion reset
