/**
 * Does:      The blog's words and numbers for the page (ADR: Every LinkedIn post is also a blog post): the post and
 *            stats shapes the API answers, the NEW POST rule, the date line, the body split into words and hashtags,
 *            where readers came from as shares, the most read, and the reports a page sends for a read and an event.
 * Does not:  Draw anything (components/blog does) or decide where a read came from (the API does, from what is sent).
 * Used by:   BlogPage, BlogPostCard, BlogCard, useBlogPosts, useBlogReport.
 */
import { linkCode } from './links';
import { request } from './request';

/** One slide or image of a post, as the API answers it. */
export type BlogImage = {
  position: number;
  url: string;
  alt_text: string;
  width: number;
  height: number;
};

/** One published post with its reads, as the API answers it. */
export type BlogPost = {
  slug: string;
  title: string;
  summary: string;
  body: string;
  published_at: string;
  linkedin_url: string;
  code_url: string | null;
  images: BlogImage[];
  tags: string[];
  reads: number;
  reads7d: number;
};

/** One post's numbers on the Admin card, every one from the blog's views. */
export type BlogStat = {
  post_id: number;
  title: string;
  published_at: string;
  reads: number;
  readers: number;
  reads7d: number;
  from_tagged_links: number;
  last_read: string | null;
  average_depth: number | null;
  average_seconds: number | null;
  to_linked_in: number;
  to_code: number;
  past_half: number;
  finished: number;
  clicked_out: number;
};

/** The Admin card's report: each post's numbers and the month's reads by source. */
export type BlogStats = {
  kept: string | null;
  available: boolean;
  posts: BlogStat[];
  sources: { source: string; reads: number }[];
};

/** A post is NEW in the side nav and on its card for this many days after LinkedIn first had it. */
export const NEW_FOR_DAYS = 7;

const DAY_MS = 86_400_000;

/** True while the newest post is under a week old: the side nav's NEW POST badge. */
export function isNew(publishedAt: string | undefined, nowMs: number): boolean {
  if (publishedAt === undefined) return false;
  const at = Date.parse(publishedAt);
  return Number.isFinite(at) && nowMs - at >= 0 && nowMs - at < NEW_FOR_DAYS * DAY_MS;
}

/** The date a post was first on LinkedIn, as the card prints it: "Oct 19, 2026", read in Central time. */
export function postDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'America/Chicago',
  });
}

/** A post's text in pieces: plain words, and the hashtags the card colours. */
export function bodyParts(body: string): { tag: boolean; text: string }[] {
  return body
    .split(/(#[A-Za-z][A-Za-z0-9_]*)/)
    .filter((text) => text !== '')
    .map((text) => ({ tag: /^#[A-Za-z]/.test(text), text }));
}

/** What a source is called on the page. */
export const SOURCE_WORDS: Record<string, string> = {
  linkedin: 'LinkedIn',
  ref: 'Tagged links',
  search: 'Search',
  direct: 'Direct',
};

/** Where readers came from, largest first, each a whole percent of the reads; empty when nothing was read. */
export function sourceShares(
  sources: { source: string; reads: number }[]
): { source: string; label: string; reads: number; share: number }[] {
  const total = sources.reduce((sum, s) => sum + s.reads, 0);
  if (total === 0) return [];
  return [...sources]
    .filter((s) => s.reads > 0)
    .sort((a, b) => b.reads - a.reads || a.source.localeCompare(b.source))
    .map((s) => ({
      source: s.source,
      label: SOURCE_WORDS[s.source] ?? s.source,
      reads: s.reads,
      share: Math.round((s.reads * 100) / total),
    }));
}

/** The posts read most this week, then all time, then newest: the page's "Most read" column. */
export function mostRead(posts: BlogPost[], count = 3): BlogPost[] {
  return [...posts]
    .sort(
      (a, b) =>
        b.reads7d - a.reads7d || b.reads - a.reads || b.published_at.localeCompare(a.published_at)
    )
    .slice(0, count);
}

/** The scroll depths a reader has newly passed, from how far down the post they are (0 to 1). */
export function depthsPassed(fraction: number, already: ReadonlySet<number>): number[] {
  return [25, 50, 75, 100].filter((mark) => fraction * 100 >= mark && !already.has(mark));
}

/** The read the page reports when a post opens: the slug, the referring page, and the address's code and utm fields. */
export function readReport(slug: string, search: string, referrer: string) {
  const params = new URLSearchParams(search);
  return {
    slug,
    referrer: referrer === '' ? null : referrer,
    ref: linkCode(search),
    utm_source: params.get('utm_source'),
    utm_medium: params.get('utm_medium'),
    utm_campaign: params.get('utm_campaign'),
  };
}

async function readJson<T>(url: string, signal?: AbortSignal): Promise<T | null> {
  const response = await request(url, { signal }, { background: true });
  if (response.status === 404 || response.status === 503) return null;
  if (!response.ok) throw new Error(`The blog API responded with ${response.status}`);
  return (await response.json()) as T;
}

/** Every published post, newest first; null when the blog cannot be read now. */
export async function fetchPosts(signal?: AbortSignal): Promise<BlogPost[] | null> {
  return (await readJson<{ posts: BlogPost[] }>('/api/blog/posts', signal))?.posts ?? null;
}

/** The Admin card's numbers and the page's sources column. */
export function fetchBlogStats(signal?: AbortSignal): Promise<BlogStats | null> {
  return readJson<BlogStats>('/api/admin/blog', signal);
}

/** Sends one report and forgets it: a lost read or event costs a number, never the page. */
export function sendBlogReport(path: '/api/blog/reads' | '/api/blog/events', body: object): void {
  void request(
    path,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      keepalive: true,
    },
    { background: true }
  ).catch(() => {});
}
