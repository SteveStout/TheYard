/**
 * Does:      Says what the blog's page head shows (ADR-142, the addendum on the landing views): on the list, "Blog", its
 *            line, and an eyebrow that counts the posts; on a post, the post's own title as the page's one h1, its summary
 *            as the line, its cover, and an eyebrow with its date and read time once the post is read.
 * Does not:  Read a post or the list (useBlogPost.ts and useBlogList.ts do) or draw anything; no React in here.
 * Used by:   BlogPage.tsx, blogHead.test.ts.
 */
import type { BlogImage } from './blog';
import { postDate } from './blog';
import { minutesLine } from './blogFilter';

/** The list's own line under its title. */
export const BLOG_LEDE =
  'Everything I post on LinkedIn, kept here in full with the code and the live pages it talks about. Newest first.';

/** What a page head shows: the eyebrow's parts, the title, the line under it, and a post's cover. */
export type BlogHead = {
  eyebrow: string[];
  title: string;
  lede: string | null;
  cover: BlogImage | null;
};

/** The list's head: the eyebrow counts the posts when the count is known. */
export function listHead(total: number | null): BlogHead {
  return {
    eyebrow: ['Steven Stout', 'Blog', ...(total === null ? [] : [`${total} posts, newest first`])],
    title: 'Blog',
    lede: BLOG_LEDE,
    cover: null,
  };
}

/** What a post's head is drawn from: the post once read, or the hook the server drew it with. */
export type HeadPost = {
  title: string;
  summary: string;
  image: BlogImage | null;
  published_at?: string;
  reading_minutes?: number;
};

/** A post's head: its own title, its summary, its cover, and its date and read time when they are known. */
export function postHead(post: HeadPost | null): BlogHead {
  const when =
    post?.published_at === undefined || post.reading_minutes === undefined
      ? []
      : [`${postDate(post.published_at)} · ${minutesLine(post.reading_minutes)}`];
  return {
    eyebrow: ['Steven Stout', 'Blog', ...when],
    title: post?.title ?? 'Blog',
    lede: post?.summary ?? null,
    cover: post?.image ?? null,
  };
}
