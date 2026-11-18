/**
 * Does:      The blog's filter as the address carries it (ADR: Every LinkedIn post is also a blog post, the addendum on
 *            the read model): read from a query, written back in one order, turned into the list's API address; and
 *            the words a search shows, the match line, the marks on the widened terms, the sentence for an empty
 *            result, and the reading time. The terms to mark come from the API, which owns the synonyms.
 * Does not:  Read or write the address bar (useAddressBar.ts does), fetch (useBlogList.ts does), or decide what a
 *            word matches (the API's KeywordRules do).
 * Used by:   useBlogList.ts, useBlogFilter.ts, BlogFilters, BlogMini, BlogPostCard, BlogPage.
 */

/** What the list is filtered by. A part left null, or an empty search, is no filter on it. */
export type BlogFilter = {
  q: string;
  topic: string | null;
  keyword: string | null;
  month: string | null;
  sort: 'new' | 'read';
  year?: number;
};

/** Every post, newest first. */
export const NO_FILTER: BlogFilter = {
  q: '',
  topic: null,
  keyword: null,
  month: null,
  sort: 'new',
};

/** The address's parts that are the filter, in the one order they are written. */
export const FILTER_PARTS = ['topic', 'keyword', 'q', 'month', 'year', 'sort'] as const;

const KEY = /^[a-z-]{1,40}$/;
const MONTH = /^\d{4}-(0[1-9]|1[0-2])$/;

/** The filter an address carries; a part in a shape it cannot have is left out, never refused. */
export function filterFromSearch(search: string | URLSearchParams): BlogFilter {
  const params = typeof search === 'string' ? new URLSearchParams(search) : search;
  const key = (name: string) => {
    const value = params.get(name);
    return value !== null && KEY.test(value) ? value : null;
  };
  const month = params.get('month');
  const year = Number(params.get('year'));
  const filter: BlogFilter = {
    q: (params.get('q') ?? '').slice(0, 100),
    topic: key('topic'),
    keyword: key('keyword'),
    month: month !== null && MONTH.test(month) ? month : null,
    sort: params.get('sort') === 'read' ? 'read' : 'new',
  };
  return Number.isInteger(year) && year >= 2000 && year <= 2999 ? { ...filter, year } : filter;
}

/** The filter's parts as address parameters: only the parts that are set, the search trimmed. */
export function filterToParams(filter: BlogFilter): URLSearchParams {
  const params = new URLSearchParams();
  if (filter.topic) params.set('topic', filter.topic);
  if (filter.keyword) params.set('keyword', filter.keyword);
  if (filter.q.trim()) params.set('q', filter.q.trim());
  if (filter.month) params.set('month', filter.month);
  if (filter.year) params.set('year', String(filter.year));
  if (filter.sort === 'read') params.set('sort', 'read');
  return params;
}

/** True when any part narrows or reorders the list. */
export function isFiltered(filter: BlogFilter): boolean {
  return filterToParams(filter).toString() !== '';
}

/** The list's API address for a filter, and for the page after a cursor. */
export function listPath(filter: BlogFilter, after?: string): string {
  const params = filterToParams(filter);
  if (after) params.set('after', after);
  const query = params.toString();
  return query ? `/api/blog/posts?${query}` : '/api/blog/posts';
}

/** The slide a ?view=blog&post=...&slide=3 address opens the viewer on; null for none. */
export function slideFrom(search: string): number | null {
  const params = new URLSearchParams(search);
  const slide = Number(params.get('slide'));
  return params.get('post') !== null && Number.isInteger(slide) && slide >= 1 && slide <= 60
    ? slide
    : null;
}

/** "Matched on Microsoft Copilot · 24 mentions in the text", five places at most; null when not searching. */
export function matchLine(matched: readonly string[]): string | null {
  return matched.length === 0 ? null : `Matched on ${matched.slice(0, 5).join(' · ')}`;
}

/** A run of text, marked when a search's term landed on it. */
export type MarkedPart = { text: string; mark: boolean };

const WORD = /[a-z0-9#]/i;

/**
 * The text cut into runs, each widened term marked where it lands on the start of a word, through to the word's end,
 * the way the search matches it; a phrase is marked whole. A term with a character no word holds marks nothing.
 */
export function marks(text: string, terms: readonly string[]): MarkedPart[] {
  const usable = terms
    .filter((term) => /^[.]?[a-z0-9#]+( [a-z0-9#]+)*$/i.test(term))
    .sort((a, b) => b.length - a.length);
  if (usable.length === 0) return [{ text, mark: false }];
  const lower = text.toLowerCase();
  const parts: MarkedPart[] = [];
  let plain = 0;
  let at = 0;
  while (at < text.length) {
    const edge = at === 0 || !WORD.test(text[at - 1]);
    const term = edge ? usable.find((t) => lower.startsWith(t.toLowerCase(), at)) : undefined;
    if (term === undefined) {
      at++;
      continue;
    }
    let end = at + term.length;
    if (!term.includes(' ')) while (end < text.length && /[a-z0-9]/i.test(text[end])) end++;
    if (at > plain) parts.push({ text: text.slice(plain, at), mark: false });
    parts.push({ text: text.slice(at, end), mark: true });
    plain = at = end;
  }
  if (plain < text.length) parts.push({ text: text.slice(plain), mark: false });
  return parts;
}

/** The chips' labels by key, from the list's own answer, for the sentence an empty result says. */
export type FilterLabels = {
  topics: Record<string, string>;
  keywords: Record<string, string>;
  months: Record<string, string>;
};

/** What an empty result says: what was asked, in the reader's words. */
export function emptyLine(filter: BlogFilter, labels: FilterLabels): string {
  const asked = [
    filter.q.trim() ? `"${filter.q.trim()}"` : '',
    filter.topic ? `in ${labels.topics[filter.topic] ?? filter.topic}` : '',
    filter.keyword ? `about ${labels.keywords[filter.keyword] ?? filter.keyword}` : '',
    filter.month ? `in ${labels.months[filter.month] ?? filter.month}` : '',
  ].filter(Boolean);
  return `No post matches${asked.length ? ` ${asked.join(' ')}` : ''}.`;
}

/** "16 min read". */
export function minutesLine(minutes: number): string {
  return `${minutes} min read`;
}
