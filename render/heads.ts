/**
 * Does:      The head a link preview reads for the pages a reader is sent to (ADR: The public face): the review
 *            route, the blog, one post, About Steven and Quality each get their own title, description and address
 *            in every tag an unfurler reads, in the pages' own words. Every other address keeps the head the
 *            build wrote.
 * Does not:  Read anything itself (the renderer hands it the posts it keeps), or touch the picture: the preview
 *            card stays the one the build draws.
 */

/** The address every canonical link is written against. */
export const SITE = 'https://theyard.stevenstout.biz';

import type { BlogImage } from '../src/lib/blog';

/** What a post lends its preview, and its hook's first image (ADR-132). */
export type PostHead = { slug: string; title: string; summary: string; image?: BlogImage | null };

/** One page's head: its title, a description of 160 characters or fewer, and its own address. */
export type Head = { title: string; description: string; path: string };

const REVIEW: Head = {
  title: 'Check my work in ten minutes',
  description:
    'Seven stops, ten minutes. Every stop is the running system, and every number on it is read live.',
  path: '/?view=review',
};

const BLOG: Head = {
  title: 'Blog',
  description:
    'Everything I post on LinkedIn, kept here in full with the code and the live pages it talks about.',
  path: '/?view=blog',
};

const AUTHOR: Head = {
  title: 'About Steven',
  description:
    'Lead / Staff .NET engineer. St. Louis area, Central time, fully remote for seven years.',
  path: '/?doc=author',
};

const QUALITY: Head = {
  title: 'Quality you can watch',
  description:
    'AI made me fast. For two weeks in October it also let the code slip where no test was looking, so I built a ratchet.',
  path: '/?doc=quality',
};

/** Cut at a word so the description stays inside what unfurlers show. */
function clip(text: string, limit = 160): string {
  if (text.length <= limit) return text;
  const cut = text.slice(0, limit - 3);
  return `${cut.slice(0, cut.lastIndexOf(' '))}...`;
}

/**
 * The head for an address, or null when the build's own head is right for it.
 * @param search the address's query, with or without its "?"
 * @param posts the published posts the renderer keeps
 */
export function headFor(search: string, posts: readonly PostHead[]): Head | null {
  const query = new URLSearchParams(search.replace(/^\?/, ''));
  const view = query.get('view');
  const doc = query.get('doc');
  if (view === 'review') return REVIEW;
  if (view === 'blog') {
    const post = posts.find((each) => each.slug === query.get('post'));
    return post
      ? {
          title: post.title,
          description: clip(post.summary),
          path: `/?view=blog&post=${encodeURIComponent(post.slug)}`,
        }
      : BLOG;
  }
  if (doc === 'author') return AUTHOR;
  if (doc === 'quality') return QUALITY;
  return null;
}

const attribute = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/**
 * The page's head with this page's title, description and address in place of the build's, in the title and in
 * every description, address and title tag an unfurler reads.
 * @param html the page as the API serves it, or the part of it above #root
 * @param head this page's head
 */
export function withHead(html: string, head: Head): string {
  const title = attribute(`${head.title} | Steven Stout`);
  const description = attribute(head.description);
  const address = attribute(SITE + head.path);
  return html
    .replace(/<title>[^<]*<\/title>/, () => `<title>${title}</title>`)
    .replace(
      /(<meta\s+name="description"\s+content=")[^"]*(")/,
      (_, open: string, close: string) => open + description + close
    )
    .replace(
      /(<link rel="canonical" href=")[^"]*(")/,
      (_, open: string, close: string) => open + address + close
    )
    .replace(
      /(<meta property="og:url" content=")[^"]*(")/,
      (_, open: string, close: string) => open + address + close
    )
    .replace(
      /(<meta property="og:title" content=")[^"]*(")/,
      (_, open: string, close: string) => open + title + close
    )
    .replace(
      /(<meta\s+property="og:description"\s+content=")[^"]*(")/,
      (_, open: string, close: string) => open + description + close
    )
    .replace(
      /(<meta name="twitter:title" content=")[^"]*(")/,
      (_, open: string, close: string) => open + title + close
    )
    .replace(
      /(<meta\s+name="twitter:description"\s+content=")[^"]*(")/,
      (_, open: string, close: string) => open + description + close
    );
}
