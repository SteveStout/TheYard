// render/server.mjs. The rendering service's door on App Service (ADR: A rendering service beside the API).
// Does:      Listens for HTTP, hands each page request to the renderer the build made (dist-render/entry.js), and streams
//            the answer back compressed, flushed as it is written so the top of the page leaves at once.
// Does not:  Draw, read the API or decide anything about a page (render.ts does), or serve assets and /api, which the
//            edge sends to the API. A Cloudflare Worker is the same renderer behind a different door.
// Used by:   render/Dockerfile (its start command), and playwright.config.ts for the browser suite.
import { createServer } from 'node:http';
import { setDefaultAutoSelectFamilyAttemptTimeout } from 'node:net';
import { Readable, pipeline } from 'node:stream';
import { constants, createBrotliCompress, createGzip } from 'node:zlib';
import { createRenderer, servedPages } from '../dist-render/entry.js';

// #region settings
/** Where the service listens: App Service sends the container's traffic to 8080 (WEBSITES_PORT). */
const port = Number(process.env.PORT ?? 8080);
/**
 * The sites this service draws for, name=origin, comma-separated, the first one
 * the site the bare path draws: "sql=https://app-theyard-ss-...,cosmos=https://...".
 * Each name is a path, /site/<name>, which the edge's rule for that domain names.
 */
const sites = new Map(
  (process.env.YARD_SITES ?? 'sql=http://localhost:5210')
    .split(',')
    .map((pair) => pair.split('=').map((part) => part.trim()))
    .filter(([name, origin]) => name && origin)
);
/** The build this image was made from, written by the deploy as it is for the API (ADR-005). */
const own = {
  version: process.env.APP_VERSION ?? 'dev',
  commit: process.env.APP_COMMIT ?? 'local',
};
/** Only for the browser suite on one machine: every request that is not a page goes on to this origin. */
const passOrigin = process.env.YARD_PASS_ORIGIN ?? null;
// #endregion settings

const render = createRenderer(own, Number(process.env.YARD_DEADLINE_MS ?? 2500));
// A name with two addresses (localhost is ::1 and 127.0.0.1) is tried one address at a time, 250 ms
// each by default, and a page being drawn holds the event loop past that, so a read of a healthy
// API failed as a timeout in the browser suite. Five seconds an address is what a draw never holds.
setDefaultAutoSelectFamilyAttemptTimeout(5_000);
const pageAt = servedPages();

// #region readiness
/**
 * Ready when every site's API has answered with its page and its build: until
 * then a page request could only be a 503. The answer says, per site, whether
 * the API runs this service's build, which is when pages are drawn here.
 */
async function readiness() {
  const reads = await Promise.all(
    [...sites].map(async ([name, origin]) => {
      const page = await pageAt(origin);
      const same = page !== null && page.version === own.version && page.commit === own.commit;
      return { name, answered: page !== null, api_build: page?.version ?? null, same_build: same };
    })
  );
  return { ready: reads.every((site) => site.answered), build: own, sites: reads };
}
// #endregion readiness

// #region answer
/** The request's headers as the Fetch API holds them; a repeated header is joined, as HTTP allows. */
function headersOf(request) {
  const headers = new Headers();
  for (const [name, value] of Object.entries(request.headers)) {
    if (value !== undefined) headers.set(name, Array.isArray(value) ? value.join(', ') : value);
  }
  return headers;
}

/**
 * Sends a Fetch API response through Node's response, compressed when the
 * browser asks, and flushed on every part the renderer writes: a compressor
 * that waits for a full block would hold back the top of the page it was
 * written early to send.
 */
function send(request, response, answer) {
  const headers = Object.fromEntries(answer.headers);
  const accepts = String(request.headers['accept-encoding'] ?? '');
  const squeeze =
    answer.body === null || request.method === 'HEAD'
      ? null
      : accepts.includes('br')
        ? createBrotliCompress({
            flush: constants.BROTLI_OPERATION_FLUSH,
            params: { [constants.BROTLI_PARAM_QUALITY]: 5 },
          })
        : accepts.includes('gzip')
          ? createGzip({ flush: constants.Z_SYNC_FLUSH })
          : null;
  if (squeeze) {
    headers['content-encoding'] = accepts.includes('br') ? 'br' : 'gzip';
    delete headers['content-length'];
  }
  response.writeHead(answer.status, headers);
  if (answer.body === null || request.method === 'HEAD') return response.end();
  // pipeline, not pipe: a stream that fails part-way ends this response and is logged, where a
  // failure in a bare pipe is an error nobody listens for, which ends the whole process.
  const parts = squeeze
    ? [Readable.fromWeb(answer.body), squeeze, response]
    : [Readable.fromWeb(answer.body), response];
  pipeline(...parts, (error) => {
    if (error) console.error('send:', error.message);
  });
}

/** The browser suite's one-machine stand-in for the edge: anything but a page goes on to the site. */
async function passOn(request, response) {
  const body = request.method === 'GET' || request.method === 'HEAD' ? undefined : request;
  const answer = await fetch(`${passOrigin}${request.url}`, {
    method: request.method,
    headers: headersOf(request),
    body,
    duplex: 'half',
    redirect: 'manual',
  });
  const headers = Object.fromEntries(answer.headers);
  delete headers['content-encoding'];
  delete headers['content-length'];
  response.writeHead(answer.status, headers);
  if (answer.body === null) return response.end();
  pipeline(Readable.fromWeb(answer.body), response, (error) => {
    if (error) console.error('pass:', error.message);
  });
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? '/', 'http://render.local');
    if (url.pathname === '/healthz') {
      response.writeHead(200, { 'content-type': 'text/plain' });
      return response.end('ok');
    }
    if (url.pathname === '/readyz') {
      const state = await readiness();
      response.writeHead(state.ready ? 200 : 503, { 'content-type': 'application/json' });
      return response.end(JSON.stringify(state));
    }
    const named = /^\/site\/([a-z]+)\/?$/.exec(url.pathname);
    const isPage = request.method === 'GET' || request.method === 'HEAD';
    if (isPage && (url.pathname === '/' || named)) {
      const origin = named ? sites.get(named[1]) : sites.values().next().value;
      if (!origin) {
        response.writeHead(404, { 'content-type': 'text/plain' });
        return response.end('No such site here.');
      }
      return send(request, response, await render(url.href, headersOf(request), origin));
    }
    if (passOrigin) return await passOn(request, response);
    response.writeHead(404, { 'content-type': 'text/plain' });
    response.end('This service answers pages only; the edge sends everything else to the API.');
  } catch (error) {
    console.error('request:', error);
    if (!response.headersSent) response.writeHead(500, { 'content-type': 'text/plain' });
    response.end();
  }
});
// #endregion answer

server.listen(port, () => {
  console.warn(`TheYard rendering service ${own.version} on ${port}, sites ${[...sites.keys()]}`);
});
