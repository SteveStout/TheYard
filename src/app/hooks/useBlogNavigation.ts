/**
 * Does:      Opens the blog and a post on it, goes from a post back to the list, and moves the blog's own part of the
 *            address (the list's filter, the slide a post opens on): a filter is a history entry so Back works, a
 *            search being typed and a slide being turned replace the entry they are on.
 * Does not:  Close the other views' state of its own accord beyond what the blog covers, or write the address bar itself.
 * Used by:   useNavigation.ts.
 */
import type { Address } from './useAddressBar';

/** The blog's four moves (ADR: Every LinkedIn post is also a blog post), the same shape as Account's. */
export function useBlogNavigation(address: Address, closeOthers: () => void) {
  const blogParams = (slug: string | null) => {
    const params = new URLSearchParams();
    params.set('view', 'blog');
    if (slug !== null) params.set('post', slug);
    return params;
  };

  /** The blog's own page, or one post on it, with its own part of the address; pushed, so Back returns to where it was opened from. */
  const openBlog = (slug: string | null = null, query = '') => {
    const params = blogParams(slug);
    new URLSearchParams(query).forEach((value, key) => params.set(key, value));
    address.push({ viaBlog: true }, params);
    closeOthers();
    address.setBlogPost(slug);
    address.setBlogQuery(query);
    address.setBlogOpen(true);
  };

  /** The blog's own part of the address: a new entry for a filter chosen, the same entry for a search typed or a slide turned. */
  const moveBlog = (query: string, how: 'push' | 'replace') => {
    const params = blogParams(address.blogPost);
    new URLSearchParams(query).forEach((value, key) => params.set(key, value));
    if (how === 'push') address.push({ viaBlog: true }, params);
    else address.replace(address.entry() ?? { viaBlog: true }, params);
    address.setBlogQuery(query);
  };

  /** From a post to the list: back when the list pushed the post, else the list in place. */
  const closeBlogPost = () => {
    if (address.entry()?.viaBlog && address.blogPost !== null) {
      address.stepBack();
      return;
    }
    address.replaceQuery(blogParams(null));
    address.setBlogPost(null);
    address.setBlogQuery('');
  };

  return { openBlog, closeBlogPost, moveBlog };
}
