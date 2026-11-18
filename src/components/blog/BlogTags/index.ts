/**
 * Does:      The folder entry for BlogTags: re-exports BlogTags and Marked, so an import names the folder,
 *            components/blog/BlogTags.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   BlogMini.tsx, BlogPostCard.tsx.
 */
export { BlogTags, Marked } from './BlogTags';
