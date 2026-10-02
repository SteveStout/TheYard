/**
 * The account view, at ?view=account: the page head, then one of three parts,
 * each in its own folder beside this one. This file only chooses which part to
 * show, so it reads as the list of what the account view can be.
 */
import type { Account } from '../../../lib/auth';
import { ResetPasswordForm } from '../ResetPasswordForm'; // a reset link was followed: choose a new password
import { SignedInAccount } from '../SignedInAccount'; // signed in: who you are, sign out, your bids
import { SignInForm } from '../SignInForm'; // signed out: sign in, register, or ask for a reset link
import styles from './AccountPanel.module.css';

/** What the account view needs from the app: the account, the ways to change it and leave, and any reset token. */
interface AccountPanelProps {
  account: Account;
  onAccountChange: (account: Account) => void;
  onOpenVehicle: (vehicleId: string) => void;
  onBack: () => void;
  /** Where the back button goes, as its label says. */
  backTo?: 'home' | 'inventory';
  /** A reset link's token, read from the address bar when the page loaded; null when there is none. */
  resetToken?: string | null;
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
  backTo = 'inventory',
  resetToken = null,
}: AccountPanelProps) {
  return (
    <section className={styles.wrap} aria-label="Account">
      {/* The same head as the Admin tab, so a view switch looks like a view
          switch. The h1 belongs here rather than inside either branch: signing
          in changes what the page offers, not which page you are on. */}
      <div className={styles.head}>
        <h1 className={styles.title}>Account</h1>
        <button type="button" className={styles.back} onClick={onBack}>
          {backTo === 'home' ? 'Back to home' : 'Back to inventory'}
        </button>
      </div>
      {account.signedIn ? (
        <SignedInAccount
          account={account}
          onAccountChange={onAccountChange}
          onOpenVehicle={onOpenVehicle}
        />
      ) : resetToken !== null ? (
        <ResetPasswordForm token={resetToken} onAccountChange={onAccountChange} />
      ) : (
        <SignInForm onAccountChange={onAccountChange} />
      )}
    </section>
  );
}
