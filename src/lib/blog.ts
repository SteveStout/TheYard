/**
 * Does:      The blog's words and numbers for the page (ADR: Every LinkedIn post is also a blog post): the card, list,
 *            post and stats shapes the API answers, the reads of a list page and of one post, the NEW POST rule, the
 *            date line, where readers came from as shares, the most read, and the reports a page sends.
 * Does not:  Draw anything (components/blog does), read the text's marks (blogBody.ts does) or decide where a read
 *            came from (the API does, from what is sent).
 * Used by:   BlogPostCard, BlogMini, BlogFilters, SlideViewer, BlogCard, useBlogPosts, useBlogList, useBlogPost,
 *            useBlogReport, blogReads.ts, blogFilter.ts, blogHead.ts, AboutWrite.
 */
import { linkCode } from './links';
import { request } from './request';

/** One slide or image of a post, as the API answers it: the full picture and its 320 px thumbnail. */
export type BlogImage = {
  position: number;
  url: string;
  thumb_url: string;
  alt_text: string;
  width: number;
  height: number;
};

/** A card's cover: the first slide, its thumbnail and its size. */
export type BlogCover = { url: string; thumb_url: string; width: number; height: number };

/** One post as a card shows it: no body, the cover, the counts, and where a search matched. */
export type BlogCard = {
  slug: string;
  title: string;
  summary: string;
  published_at: string;
  cover: BlogCover | null;
  slides: number;
  reading_minutes: number;
  topics: string[];
  keywords: string[];
  reads: number;
  reads7d: number;
  matched: string[];
};

/** One chip: the key the address carries, its label, and how many posts it would leave. */
export type BlogFacet = { key: string; label: string; count: number };

/** One page of the list as the API answers it. */
export type BlogList = {
  cards: BlogCard[];
  total: number;
  topics: BlogFacet[];
  keywords: BlogFacet[];
  months: BlogFacet[];
  next: string | null;
  terms: string[];
};

/** One post as its own page shows it, as the API answers it. */
export type BlogPost = {
  slug: string;
  title: string;
  summary: string;
  body_html: string;
  published_at: string;
  linkedin_url: string;
  code_url: string | null;
  images: BlogImage[];
  topics: string[];
  keywords: string[];
  reading_minutes: number;
  reads: number;
  reads7d: number;
  older: BlogCard | null;
  newer: BlogCard | null;
  related: BlogCard[];
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
  /** The pictures kept for the posts: how many (null when the listing was not read), their bytes, and where that was read. */
  pictures?: { count: number | null; bytes: number; source: string };
};

/** The Blog analytics card's pictures line: "4 pictures kept, 0.9 MB". */
export function picturesLine(pictures: NonNullable<BlogStats['pictures']>): string {
  if (pictures.count === null) return `Pictures: not read (${pictures.source}).`;
  const size =
    pictures.bytes < 1_000_000
      ? `${Math.round(pictures.bytes / 1000)} KB`
      : `${(pictures.bytes / 1_000_000).toFixed(1)} MB`;
  return `${pictures.count === 1 ? '1 picture' : `${pictures.count} pictures`} kept, ${size}.`;
}

/**
 * A post's body with each list item that is only a link marked data-link-row, so a phone can give that link a
 * thumb's height (WCAG 2.2 target size) without touching a link that sits inside a sentence.
 */
export function markLinkRows(html: string): string {
  return html.replace(
    /<li>(\s*<a\s[^>]*>(?:(?!<\/?a[\s>]).)*<\/a>\s*)<\/li>/gs,
    '<li data-link-row="">$1</li>'
  );
}

/** The widths every slide's thumbnail is cut at (scripts/blog_thumbs.mjs; BlogImageNames.ThumbWidths). */
export const THUMB_WIDTHS = [320, 480, 640] as const;

/**
 * A slide thumbnail's srcset, so a phone's sharper screen takes a wider cut: the 320 px file the API names and its
 * 480 and 640 px siblings beside it. A width not cut yet is answered with the full slide for five minutes, so the set
 * is never a broken picture. Empty for an address that is not a 320 px thumbnail.
 */
export function thumbSrcSet(thumbUrl: string): string {
  if (!/-320\.webp$/.test(thumbUrl)) return '';
  return THUMB_WIDTHS.map(
    (width) => `${thumbUrl.replace(/-320\.webp$/, `-${width}.webp`)} ${width}w`
  ).join(', ');
}

/** A post is NEW in the side nav and on its card for this many days after LinkedIn first had it. */
export const NEW_FOR_DAYS = 7;

const DAY_MS = 86_400_000;

/** True while the newest post is under a week old: the side nav's NEW POST badge. */
export function isNew(publishedAt: string | undefined, nowMs: number): boolean {
  if (publishedAt === undefined) return false;
  const at = Date.parse(publishedAt);
  return Number.isFinite(at) && nowMs - at >= 0 && nowMs - at < NEW_FOR_DAYS * DAY_MS;
}

/** The blog's own address, every post. */
export const ALL_POSTS_ADDRESS = '/?view=blog';

/** A post's address on this site, the one the blog opens it at. */
export function postLink(slug: string): string {
  return `${ALL_POSTS_ADDRESS}&post=${encodeURIComponent(slug)}`;
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
export function mostRead(posts: BlogCard[], count = 3): BlogCard[] {
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

/** The first page of the list, newest first, with nothing filtered; null when the blog cannot be read now. */
export async function fetchPosts(signal?: AbortSignal): Promise<BlogList | null> {
  return readJson<BlogList>('/api/blog/posts', signal);
}

/** One page of the list at an API address listPath made; null when the blog cannot be read now. */
export function fetchList(path: string, signal?: AbortSignal): Promise<BlogList | null> {
  return readJson<BlogList>(path, signal);
}

/** The API address of one post's page. */
export function fetchedFor(slug: string): string {
  return `/api/blog/posts/${encodeURIComponent(slug)}`;
}

/** One post's page; null when no published post has the slug or the blog cannot be read now. */
export function fetchPost(slug: string, signal?: AbortSignal): Promise<BlogPost | null> {
  return readJson<BlogPost>(fetchedFor(slug), signal);
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
