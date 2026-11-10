/**
 * Does:      The folder entry for BlogPage: re-exports BlogPage, so an import names the folder,
 *            components/blog/BlogPage.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   App.tsx, LatestPosts.tsx.
 */
export { BlogMini, BlogPage } from './BlogPage';
