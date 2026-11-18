import type { Page, Route } from '@playwright/test';

/**
 * A stand-in for the blog's two public reads, so a browser test checks the page's wiring whatever the local store
 * holds: the list filtered by topic, keyword, month and a few typed words, a page at a time, with its chips and their
 * counts; and one post with its older, newer and related posts. The search here knows only the words the tests type;
 * the real widening and ranking are the API's, held by BlogSearchRulesTests and BlogListTests.
 */
export type MadePost = {
  slug: string;
  title: string;
  summary: string;
  published_at: string;
  topics: string[];
  keywords: string[];
  reads: number;
  reads7d: number;
  slides: number;
};

const DAY = 86_400_000;

const TOPICS: [string, string][] = [
  ['ai', 'AI'],
  ['architecture', 'Architecture'],
  ['dotnet', '.NET'],
  ['azure', 'Azure'],
  ['cost', 'Cloud cost'],
  ['testing', 'Testing'],
  ['linux', 'Linux'],
  ['security', 'Security'],
  ['career', 'Career'],
  ['job-search', 'Job search'],
];

const KEYWORDS: Record<string, string> = {
  copilot: 'Microsoft Copilot',
  microsoft: 'Microsoft',
  claude: 'Claude',
  cqrs: 'CQRS',
};

/** The words the tests type, and what each widens to, the way the API's synonyms do for them. */
const WIDEN: Record<string, string[]> = {
  copilot: ['copilot', 'microsoft copilot', 'github copilot'],
  ms: ['microsoft'],
  scams: ['scams', 'scam', 'fraud', 'phishing', 'fake'],
  llm: ['llm', 'ai', 'claude', 'copilot', 'openai'],
};

/** A made-up post, the given days old. */
export function made(
  slug: string,
  title: string,
  ageDays: number,
  extra: Partial<MadePost> = {}
): MadePost {
  return {
    slug,
    title,
    summary: `Two lines about ${title.toLowerCase()}.`,
    published_at: new Date(Date.now() - ageDays * DAY).toISOString(),
    topics: ['dotnet'],
    keywords: [],
    reads: 1,
    reads7d: 1,
    slides: 3,
    ...extra,
  };
}

function images(post: MadePost) {
  return Array.from({ length: post.slides }, (_, n) => ({
    position: n + 1,
    url: '/api/images/badges/steve-176.jpg',
    thumb_url: '/api/images/badges/steve-176.jpg',
    alt_text: `Slide ${n + 1}`,
    width: 176,
    height: 220,
  }));
}

function card(post: MadePost, matched: string[] = []) {
  const first = images(post)[0];
  return {
    slug: post.slug,
    title: post.title,
    summary: post.summary,
    published_at: post.published_at,
    cover: first
      ? { url: first.url, thumb_url: first.thumb_url, width: first.width, height: first.height }
      : null,
    slides: post.slides,
    reading_minutes: 3,
    topics: post.topics,
    keywords: post.keywords,
    reads: post.reads,
    reads7d: post.reads7d,
    matched,
  };
}

/** Where a typed query lands in a post, or null when one of its words lands nowhere. */
function matchedIn(post: MadePost, q: string): string[] | null {
  const words = q
    .toLowerCase()
    .split(/\s+/)
    .filter((w) => w.length > 1);
  const places = new Set<string>();
  for (const word of words) {
    const terms = WIDEN[word] ?? [word];
    const text = `${post.title} ${post.summary}`.toLowerCase();
    const inText = terms.some((term) => text.includes(term));
    const inKeyword = post.keywords.filter((k) =>
      terms.some((term) => KEYWORDS[k].toLowerCase().includes(term))
    );
    const inTopic = post.topics.filter((t) => terms.includes(t));
    if (!inText && inKeyword.length === 0 && inTopic.length === 0) return null;
    if (inText) places.add('title');
    inKeyword.forEach((k) => places.add(KEYWORDS[k]));
    inTopic.forEach((t) => places.add(TOPICS.find(([key]) => key === t)![1]));
  }
  return [...places];
}

function listAnswer(posts: MadePost[], url: URL) {
  const params = url.searchParams;
  const q = params.get('q') ?? '';
  const searched = new Map(posts.map((post) => [post.slug, q ? matchedIn(post, q) : []]));
  const keeps = (post: MadePost, except?: string) =>
    (except === 'topic' || !params.get('topic') || post.topics.includes(params.get('topic')!)) &&
    (except === 'keyword' ||
      !params.get('keyword') ||
      post.keywords.includes(params.get('keyword')!)) &&
    (except === 'month' ||
      !params.get('month') ||
      post.published_at.startsWith(params.get('month')!)) &&
    searched.get(post.slug) !== null;
  const newest = [...posts].sort((a, b) => b.published_at.localeCompare(a.published_at));
  const ordered =
    params.get('sort') === 'read' ? [...newest].sort((a, b) => b.reads - a.reads) : newest;
  const matching = ordered.filter((post) => keeps(post));
  const start = Number(params.get('after') ?? 0);
  const page = matching.slice(start, start + 10);
  const count = (key: (post: MadePost) => string[], except: string, all: string[]) =>
    all.map((k) => posts.filter((post) => keeps(post, except) && key(post).includes(k)).length);
  const months = [...new Set(posts.map((post) => post.published_at.slice(0, 7)))].sort().reverse();
  const keywordKeys = Object.keys(KEYWORDS).filter((k) =>
    posts.some((post) => post.keywords.includes(k))
  );
  const topicCounts = count(
    (post) => post.topics,
    'topic',
    TOPICS.map(([key]) => key)
  );
  const keywordCounts = count((post) => post.keywords, 'keyword', keywordKeys);
  return {
    cards: page.map((post) => card(post, searched.get(post.slug) ?? [])),
    total: matching.length,
    topics: TOPICS.map(([key, label], n) => ({ key, label, count: topicCounts[n] })),
    keywords: keywordKeys.map((key, n) => ({ key, label: KEYWORDS[key], count: keywordCounts[n] })),
    months: months.map((key) => ({
      key,
      label: key,
      count: posts.filter((post) => keeps(post, 'month') && post.published_at.startsWith(key))
        .length,
    })),
    next: start + 10 < matching.length ? String(start + 10) : null,
    terms: q
      ? q
          .toLowerCase()
          .split(/\s+/)
          .flatMap((w) => WIDEN[w] ?? [w])
      : [],
  };
}

function postAnswer(posts: MadePost[], slug: string) {
  const newest = [...posts].sort((a, b) => b.published_at.localeCompare(a.published_at));
  const at = newest.findIndex((post) => post.slug === slug);
  if (at < 0) return null;
  const post = newest[at];
  return {
    ...card(post),
    body: 'The text.',
    body_html: `<p>${post.title}.</p>\n<p>${'A line of the post, long enough to wrap on a phone and fill the card. '.repeat(14)}</p>\n<p>The code is on <a href="https://github.com/SteveStout/TheYard" target="_blank" rel="noreferrer">GitHub</a>.</p>\n`,
    linkedin_url: `https://www.linkedin.com/posts/${post.slug}`,
    code_url: 'https://github.com/SteveStout/TheYard',
    images: images(post),
    tags: post.topics,
    older: newest[at + 1] ? card(newest[at + 1]) : null,
    newer: at > 0 ? card(newest[at - 1]) : null,
    related: newest
      .filter((other) => other !== post && other.topics.some((t) => post.topics.includes(t)))
      .slice(0, 3)
      .map((other) => card(other)),
  };
}

/** Answers the blog's reads from these posts, each answer held back by delayMs, and counts every list read it answers. */
export async function answerBlog(page: Page, posts: MadePost[], delayMs = 0) {
  const asked: string[] = [];
  const hold = async () => {
    if (delayMs > 0) await new Promise((resolve) => setTimeout(resolve, delayMs));
  };
  await page.route(/\/api\/blog\/posts(\?.*)?$/, async (route: Route) => {
    const url = new URL(route.request().url());
    asked.push(url.search);
    await hold();
    await route.fulfill({ json: listAnswer(posts, url) });
  });
  await page.route(/\/api\/blog\/posts\/[a-z0-9-]+(\?.*)?$/, async (route: Route) => {
    const slug = new URL(route.request().url()).pathname.split('/').pop()!;
    const answer = postAnswer(posts, slug);
    await hold();
    await (answer
      ? route.fulfill({ json: answer })
      : route.fulfill({ status: 404, json: { title: 'No such post' } }));
  });
  await page.route('**/api/admin/blog', (route) =>
    route.fulfill({
      json: {
        kept: null,
        available: true,
        posts: [],
        sources: [
          { source: 'linkedin', reads: 6 },
          { source: 'direct', reads: 2 },
        ],
      },
    })
  );
  await page.route('**/api/blog/reads', (route) => route.fulfill({ json: { source: 'direct' } }));
  await page.route('**/api/blog/events', (route) => route.fulfill({ status: 204 }));
  return asked;
}
