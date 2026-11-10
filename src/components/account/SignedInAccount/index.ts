/**
 * Does:      The folder entry for SignedInAccount: re-exports SignedInAccount, so an import names the folder,
 *            components/account/SignedInAccount.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   AccountPanel.tsx.
 */
export * from './SignedInAccount';
