/**
 * Does:      Reads the page the API serves (index.html, built with the bundle it names) and the build the API runs, keeps
 *            them for a few seconds, and cuts the page at #root, where a drawn page goes; and keeps the published
 *            posts' titles and summaries the same way, for a post's link preview.
 * Does not:  Draw anything (render.ts does) or read a store: it asks the API what any browser can ask it.
 * Used by:   render.ts, render.test.ts.
 */

import type { PostHead } from './heads';

// #region the-page
/** The page the API serves, cut where #root's drawing goes, with the build that served it. */
export type ServedPage = {
  /** The whole page as the API serves it: what a visitor gets when nothing is drawn here. */
  html: string;
  /** Everything up to #root, sent at once so the browser starts on the stylesheet and the bundle. */
  before: string;
  /** Everything after #root, sent after the drawing and the first load. */
  after: string;
  /** The build the API reports, which this service must match before it draws (render.ts). */
  version: string;
  commit: string;
};

/** The page marks where #root starts and ends with these two comments (index.html). */
export const ROOT_START = '<!-- #region root -->';
export const ROOT_END = '<!-- #endregion root -->';

/**
 * Cuts the served page at #root. The page's own marks say where; a page
 * without them (an older build) cannot be cut, and is served as it is.
 * @param html the page the API served
 */
export function cutAtRoot(html: string): { before: string; after: string } | null {
  const start = html.indexOf(ROOT_START);
  const end = html.indexOf(ROOT_END);
  if (start < 0 || end < start) return null;
  return { before: html.slice(0, start + ROOT_START.length), after: html.slice(end) };
}

/** How long a read of the page and the build is kept: a roll is seen within this long. */
const KEEP_MS = 15_000;

/** How long a read that failed is kept, so an API that is back is drawn for again at once. */
const KEEP_FAILED_MS = 2_000;

/** The agent on every read this service makes, so the Site activity card counts them as the site's own. */
export const SELF_READ = 'TheYard-SelfRead/1 (render)';

/**
 * The page and the build at one API origin, read at most once every fifteen
 * seconds per origin. The page is read from the API itself rather than built
 * into this service, so the bundle it names is always the bundle that API
 * serves: a roll of the API changes the page here within fifteen seconds. Null
 * when the API does not answer, or answers without the marks.
 */
export function servedPages(read: typeof fetch = fetch) {
  const kept = new Map<string, { at: number; page: Promise<ServedPage | null> }>();
  return (apiOrigin: string): Promise<ServedPage | null> => {
    const hit = kept.get(apiOrigin);
    if (hit && Date.now() - hit.at < KEEP_MS) return hit.page;
    const page = readServedPage(read, apiOrigin);
    const entry = { at: Date.now(), page };
    kept.set(apiOrigin, entry);
    void page.then((answer) => {
      if (answer === null) entry.at = Date.now() - KEEP_MS + KEEP_FAILED_MS;
    });
    return page;
  };
}

/**
 * The published posts' titles and summaries at one API origin, read at most once every fifteen seconds, so a
 * post's link preview carries its own title (ADR: The public face). Empty when the blog does not answer: the
 * preview then says what the blog is.
 */
export function keptPosts(read: typeof fetch = fetch) {
  const kept = new Map<string, { at: number; posts: Promise<PostHead[]> }>();
  return (apiOrigin: string): Promise<PostHead[]> => {
    const hit = kept.get(apiOrigin);
    if (hit && Date.now() - hit.at < KEEP_MS) return hit.posts;
    const posts = read(`${apiOrigin}/api/blog/posts`, {
      headers: { 'User-Agent': SELF_READ },
      signal: AbortSignal.timeout(2_000),
    })
      .then((r) => (r.ok ? r.json() : null))
      .then((body: { posts?: PostHead[] } | null) =>
        (body?.posts ?? []).map(({ slug, title, summary }) => ({ slug, title, summary }))
      )
      .catch(() => [] as PostHead[]);
    kept.set(apiOrigin, { at: Date.now(), posts });
    return posts;
  };
}

async function readServedPage(read: typeof fetch, apiOrigin: string): Promise<ServedPage | null> {
  try {
    const headers = { 'User-Agent': SELF_READ };
    const signal = AbortSignal.timeout(10_000);
    const [build, html] = await Promise.all([
      read(`${apiOrigin}/api/version`, { headers, signal }).then((r) => (r.ok ? r.json() : null)),
      read(`${apiOrigin}/`, { headers, signal }).then((r) => (r.ok ? r.text() : null)),
    ]);
    const cut = html ? cutAtRoot(html) : null;
    if (!build || !html || !cut) return null;
    return { html, ...cut, version: String(build.version), commit: String(build.commit) };
  } catch {
    return null;
  }
}
// #endregion the-page
