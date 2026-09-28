/**
 * Does:      Knows who is signed in, by asking the API, and takes every change to that from the page.
 * Does not:  Sign anyone in or out (AccountPanel does), or hold their bids.
 * Used by:   App.tsx.
 */
import { useCallback, useEffect, useState } from 'react';
import { accountQuestion, SIGNED_OUT, type Account } from '../../lib/auth';

export function useAccount() {
  const [account, setAccount] = useState<Account>(SIGNED_OUT);
  // #region who
  // Who is signed in, if anyone. The session is an httpOnly cookie (one that
  // page scripts cannot read), so the page has to ask the API (ADR-037). A
  // failure leaves the visitor signed out, which is the safe answer.
  //
  // The answer is dropped if the account changed while the question was out.
  // Otherwise a visitor who registers quickly could be signed out again by a
  // late "nobody" answer. Every change the page makes goes through
  // changeAccount, which tells the question.
  const [whoIsSignedIn] = useState(accountQuestion);
  const changeAccount = useCallback(
    (next: Account) => {
      whoIsSignedIn.changed();
      setAccount(next);
    },
    [whoIsSignedIn]
  );
  useEffect(() => {
    whoIsSignedIn.ask(setAccount).catch(() => {});
  }, [whoIsSignedIn]);
  // #endregion who
  return { account, changeAccount };
}
