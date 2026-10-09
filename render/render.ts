/**
 * Does:      Answers one page request for the rendering service: reads what the address needs from the API, draws the site
 *            with it, and streams the page, the frame first. One function behind any door: App Service's Node server today,
 *            a Cloudflare Worker later.
 * Does not:  Listen for requests (server.mjs does), read a store, or answer anything but a page: assets and /api are the API's.
 * Used by:   entry.ts, render.test.ts.
 */
import { drawForTheService, firstLoadElement } from '../src/app/entry-server';
import { loadFirst } from './loaders';
import { servedPages, type ServedPage } from './page';

// #region the-renderer
/** The build this service was made from; it draws only for an API running the same one. */
export type OwnBuild = { version: string; commit: string };

/** What the page says about how it was made, on a header a probe or a reader can see. */
export const RENDERED_HEADER = 'X-Yard-Rendered';

/** One page request: the address, the request's headers, and the API that address is read from. */
export type Render = (url: string, headers: Headers, apiOrigin: string) => Promise<Response>;

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
  return async (url, headers, apiOrigin) => {
    const page = await pageAt(apiOrigin);
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
      return asServed(page, `other-build ${page.version}`);
    }
    const search = new URL(url).search.replace(/^\?/, '');
    const cookie = headers.get('cookie');
    const load = loadFirst(search, cookie, apiOrigin, deadlineMs, read);
    return new Response(streamPage(page, load), { status: 200, headers: pageHeaders(cookie) });
  };
}

// #region edge-copy
/** The cookie a session travels in, as the API names it (api/TheYard.Api/Tokens.cs); CacheHeaderTests holds the two names together. */
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
function asServed(page: ServedPage, why: string): Response {
  return new Response(page.html, {
    status: 200,
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-cache',
      [RENDERED_HEADER]: why,
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
function streamPage(page: ServedPage, load: Promise<Awaited<ReturnType<typeof loadFirst>>>) {
  const text = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    async start(out) {
      out.enqueue(text.encode(page.before));
      let opened = false;
      try {
        const first = await load;
        const drawing = await drawForTheService(first, (error) => console.error('draw:', error));
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
