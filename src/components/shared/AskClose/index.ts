/**
 * Does:      The folder entry for AskClose: re-exports AskClose, so an import names the folder,
 *            components/shared/AskClose.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   ReviewClose.tsx, BlogPage.tsx, RecommendationsPage.tsx.
 */
export * from './AskClose';
