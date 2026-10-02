/**
 * The signed-out account form: one email, one password, and buttons to sign in,
 * register or ask for a reset link, because both jobs ask for the same two
 * things. Its own file because it is one of the three things the account view
 * can show, with its own state. The look is the account view's stylesheet.
 */
import { useState } from 'react';
import { forgotRequest, loginRequest, registerRequest, type Account } from '../../../lib/auth';
import styles from '../AccountPanel/AccountPanel.module.css';

/** What the sign-in form needs: a way to hand the signed-in account back up. */
interface SignInFormProps {
  onAccountChange: (account: Account) => void;
}

// #region sign-in
/**
 * The sign-in and register form. A wrong password or a taken address comes back
 * as the server's one sentence under the fields; "Forgot your password?" uses
 * the same email field and answers with one sentence of its own.
 */
export function SignInForm({ onAccountChange }: SignInFormProps) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // "Forgot password": the same email field, one more button, one sentence back.
  const [forgotNote, setForgotNote] = useState<string | null>(null);

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
    <div className={`${styles.panel} op-glass`}>
      <h2 className={styles.heading}>Sign in to bid</h2>
      <p className={styles.lede}>
        Bids belong to an account, so the auction can tell two people apart. Nothing is emailed and
        nothing is shared; this is a showcase, and the address is only the name your bids are under.
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
          <button
            className={styles.secondary}
            type="button"
            disabled={busy || email.length === 0}
            data-testid="forgot-password"
            onClick={() => {
              setBusy(true);
              setForgotNote(null);
              void forgotRequest(email).then((result) => {
                setBusy(false);
                setForgotNote(result.message);
              });
            }}
          >
            Forgot your password?
          </button>
        </div>
        {forgotNote && (
          <p className={styles.hint} role="status" data-testid="forgot-note">
            {forgotNote}
          </p>
        )}
      </form>
    </div>
  );
}
// #endregion sign-in
