/**
 * Does:      Answers one page request for the rendering service: reads what the address needs from the API, draws the site
 *            with it, and streams the page, the frame first. One function behind any door: App Service's Node server today,
 *            a Cloudflare Worker later.
 * Does not:  Listen for requests (server.mjs does), read a store, or answer anything but a page: assets and /api are the API's.
 * Used by:   entry.ts, render.test.ts.
 */
import { drawForTheService, firstLoadElement } from '../src/app/entry-server';
import { loadFirst } from './loaders';
import { headFor, withHead, type PostHead } from './heads';
import { blogPostFrom } from '../src/app/firstAddress';
import { RECOMMENDATION_PLACES, recommendationsAnswerFrom } from '../src/lib/recommendations';
import type { HookPost } from '../src/hooks/useFirstLoad';
import { keptPost, servedPages, type ServedPage } from './page';
import { inlineScriptHashes, pageNonce, securityHeaders } from './security';

// #region the-renderer
/** The build this service was made from; it draws only for an API running the same one. */
export type OwnBuild = { version: string; commit: string };

/** What the page says about how it was made, on a header a probe or a reader can see. */
export const RENDERED_HEADER = 'X-Yard-Rendered';

/** One page request: the address, the request's headers, the API that address is read from, and the site's store. */
export type Render = (
  url: string,
  headers: Headers,
  apiOrigin: string,
  store?: string | null
) => Promise<Response>;

/**
 * The renderer, made once per process.
 *
 * It draws only when the API runs the build it was made from. The page it
 * draws into is the API's own index.html, which names the API's bundle, and
 * React in that bundle takes the markup over only if this service drew it from
 * the same code; a service a version behind would draw markup the bundle throws
 * away. So until the two agree, during a roll, it sends the API's page as it is
 * and the browser draws, as the site did before this service existed.
 *
 * Every failure ends the same way: the API's page, as it is. A read that is
 * slow or fails leaves that view to the browser (loaders.ts); a component that
 * throws while drawing leaves the page to the browser. The one answer that is
 * not a page is an API that does not answer at all, which nothing here can draw
 * around: a 503 that says so.
 * @param own the build this service was made from
 * @param deadlineMs how long the API reads may take before the page is drawn without them
 */
export function createRenderer(
  own: OwnBuild,
  deadlineMs = 2_500,
  read: typeof fetch = fetch
): Render {
  const pageAt = servedPages(read);
  const postAt = keptPost(read);
  // The API's page changes only with a roll, so its inline scripts' hashes are worked out once for each page read.
  const hashed = new WeakMap<ServedPage, Promise<string[]>>();
  const hashesOf = (page: ServedPage) => {
    if (!hashed.has(page)) hashed.set(page, inlineScriptHashes(page.html));
    return hashed.get(page)!;
  };
  return async (url, headers, apiOrigin, store = null) => {
    const asked = performance.now();
    const page = await pageAt(apiOrigin);
    // The page's own read is all a header can name: the reads behind the drawing finish after the headers have gone.
    const timing = `shell;dur=${Math.round((performance.now() - asked) * 10) / 10}`;
    if (!page) {
      return new Response('The API is not answering, so there is no page to send.', {
        status: 503,
        headers: {
          'Content-Type': 'text/plain; charset=utf-8',
          [RENDERED_HEADER]: 'no-api',
          'Retry-After': '10',
        },
      });
    }
    if (page.version !== own.version || page.commit !== own.commit) {
      return asServed(
        page,
        `other-build ${page.version}`,
        securityHeaders(await hashesOf(page), null)
      );
    }
    const search = new URL(url).search.replace(/^\?/, '');
    const cookie = headers.get('cookie');
    // The first load starts now, beside the read of a named post for the page's head, never after it.
    const loading = loadFirst(search, cookie, apiOrigin, deadlineMs, read, store);
    const named = blogPostFrom(new URLSearchParams(search));
    const posts = named ? [await postAt(apiOrigin, named)].filter((post) => post !== null) : [];
    const head = headFor(search, posts, await recommendationsCount(search, loading));
    const shell = head ? { ...page, before: withHead(page.before, head) } : page;
    // The post an address names goes into the first load from the posts already read for its head, so its hook is
    // drawn here and the browser asks for nothing to show it (ADR-132).
    const post = postFor(search, posts);
    const load = loading.then((first) => (post ? { ...first, post } : first));
    // A nonce for this page alone: React writes it on any script it streams, and the policy lets only it through.
    const nonce = pageNonce();
    return new Response(streamPage(shell, load, nonce), {
      status: 200,
      headers: {
        ...pageHeaders(cookie),
        ...securityHeaders(await hashesOf(page), nonce),
        'Server-Timing': timing,
      },
    });
  };
}

/**
 * How many recommendations the page's head names: on ?view=recommendations, the count of the answer the page is drawn
 * from; when that read has not answered, and on every other address, the build's own count of data/recommendations.json.
 */
export async function recommendationsCount(
  search: string,
  loading: Promise<{ recommendations?: unknown }>
): Promise<number> {
  if (new URLSearchParams(search).get('view') !== 'recommendations') return RECOMMENDATION_PLACES;
  return (
    recommendationsAnswerFrom((await loading).recommendations ?? null)?.count ??
    RECOMMENDATION_PLACES
  );
}

/** The post a ?view=blog&post= address names, as its hook draws it, or null when the address names none the blog has. */
export function postFor(search: string, posts: readonly PostHead[]): HookPost | null {
  const slug = blogPostFrom(new URLSearchParams(search));
  const post = slug === null ? undefined : posts.find((each) => each.slug === slug);
  return post
    ? { slug: post.slug, title: post.title, summary: post.summary, image: post.image ?? null }
    : null;
}

// #region edge-copy
/** The cookie a session travels in, as the API names it (api/TheYard.Api/Shared/Auth/Tokens.cs); CacheHeaderTests holds the two names together. */
export const SESSION_COOKIE = 'theyard_session';

/**
 * How long the edge may answer an anonymous page from its own copy: ten
 * seconds fresh, then twenty more while it fetches a new one behind the
 * visitor. The browser is told nothing new and still asks every time; the
 * edge alone reads this header, as it does for the catalogue's two reads.
 */
export const EDGE_RULE = 'public, max-age=10, stale-while-revalidate=20';

/**
 * What the edge keys its copy on: the whole query, because each view is one,
 * and the session cookie, so a request that carries a session never meets a
 * copy drawn for nobody. The same header goes on every page, kept or not,
 * because the edge reads the first one it sees for an address and ignores the rest.
 */
export const EDGE_VARY = `query,cookie=${SESSION_COOKIE}`;

/**
 * The headers on a drawn page. A request with no Cookie header at all is
 * everybody's: its page is the same for every such visitor and the edge may
 * keep it. A request with any cookie is somebody's, and its page is answered
 * fresh and never stored anywhere shared, whatever the cookie turns out to be.
 * @param cookie the request's Cookie header, or null when it sent none
 */
export function pageHeaders(cookie: string | null): Record<string, string> {
  const anonymous = cookie === null;
  return {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': anonymous ? 'no-cache' : 'private, no-cache',
    Vary: 'Cookie',
    'Netlify-CDN-Cache-Control': anonymous ? EDGE_RULE : 'private, no-store',
    'Netlify-Vary': EDGE_VARY,
    [RENDERED_HEADER]: 'drawn',
  };
}
// #endregion edge-copy

/** The API's page, untouched: what a visitor got before this service existed. */
function asServed(page: ServedPage, why: string, security: Record<string, string> = {}): Response {
  return new Response(page.html, {
    status: 200,
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-cache',
      [RENDERED_HEADER]: why,
      ...security,
    },
  });
}
// #endregion the-renderer

// #region the-stream
/**
 * The page in the order a browser can use it. Everything above #root goes at
 * once, before any read has answered, so the browser starts on the stylesheet,
 * the font and the bundle while the API is asked; then #root with the drawing;
 * then the first load the browser's React starts from; then the rest of the page.
 * A draw that fails before its frame is ready leaves #root empty with no first
 * load, and the browser draws it from nothing, as it did before.
 */
function streamPage(
  page: ServedPage,
  load: Promise<Awaited<ReturnType<typeof loadFirst>>>,
  nonce: string
) {
  const text = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    async start(out) {
      out.enqueue(text.encode(page.before));
      let opened = false;
      try {
        const first = await load;
        const drawing = await drawForTheService(
          first,
          (error) => console.error('draw:', error),
          nonce
        );
        out.enqueue(text.encode('\n    <div id="root" data-drawn="server">'));
        opened = true;
        const reader = drawing.getReader();
        for (let part = await reader.read(); !part.done; part = await reader.read()) {
          out.enqueue(part.value);
        }
        out.enqueue(text.encode(`</div>\n    ${firstLoadElement(first)}\n    `));
      } catch (error) {
        console.error('page:', error);
        // With no first load beside it, the browser clears whatever was drawn and draws the page itself (mount.tsx).
        out.enqueue(text.encode(opened ? '</div>\n    ' : '\n    <div id="root"></div>\n    '));
      }
      out.enqueue(text.encode(page.after));
      out.close();
    },
  });
}
// #endregion the-stream
