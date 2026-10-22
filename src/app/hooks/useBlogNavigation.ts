/**
 * Does:      Opens the blog and a post on it, and goes from a post back to the list, each a history entry so Back works.
 * Does not:  Close the other views' state of its own accord beyond what the blog covers, or write the address bar itself.
 * Used by:   useNavigation.ts.
 */
import type { Address } from './useAddressBar';

/** The blog's three moves (ADR: Every LinkedIn post is also a blog post), the same shape as Account's. */
export function useBlogNavigation(address: Address, closeOthers: () => void) {
  const blogParams = (slug: string | null) => {
    const params = new URLSearchParams();
    params.set('view', 'blog');
    if (slug !== null) params.set('post', slug);
    return params;
  };

  /** The blog's own page, or one post on it; pushed, so Back returns to where it was opened from. */
  const openBlog = (slug: string | null = null) => {
    address.push({ viaBlog: true }, blogParams(slug));
    closeOthers();
    address.setBlogPost(slug);
    address.setBlogOpen(true);
  };

  /** From a post to the list: back when the list pushed the post, else the list in place. */
  const closeBlogPost = () => {
    if (address.entry()?.viaBlog && address.blogPost !== null) {
      address.stepBack();
      return;
    }
    address.replaceQuery(blogParams(null));
    address.setBlogPost(null);
  };

  return { openBlog, closeBlogPost };
}
