import { describe, expect, it } from 'vitest';
import loadersSource from './loaders.ts?raw';
import pageSource from './page.ts?raw';
import renderSource from './render.ts?raw';
import serverSource from './server.mjs?raw';
import { cutAtRoot, ROOT_END, ROOT_START } from './page';
import { createRenderer, EDGE_RULE, EDGE_VARY, postFor, RENDERED_HEADER } from './render';
import { REVIEW_STOPS } from '../src/lib/reviewStops';
import recommendations from '../data/recommendations.json';

/** Text as React writes it into HTML: the five characters an attribute or a text node escapes. */
// As React's server render escapes text: the five characters it rewrites, a double quote among them.
const escapeHtml = (text: string) =>
  text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#x27;');

// #region a-fake-api
const API = 'https://api.example';
const OWN = { version: '1.0.3.100', commit: 'abc1234' };
const NOW = Date.now();
const PAGE = `<!doctype html><html><head><title>TheYard</title></head><body>
    ${ROOT_START}
    <div id="root" data-drawn="landing"><p>The landing page the build drew.</p></div>
    ${ROOT_END}
    <noscript>For a reader without JavaScript.</noscript></body></html>`;
const vehicle = {
  id: '4e3cd74f-bb88-4efe-b234-bcb2f7474b40',
  vin: 'CG2UAF4T8LRBBVWJY',
  year: 2025,
  make: 'Mazda',
  model: 'CX-5',
  trim: 'Turbo',
  body_style: 'SUV',
  exterior_color: 'Blue',
  interior_color: 'Light Grey',
  engine: '2.5L I4',
  transmission: 'automatic',
  drivetrain: 'FWD',
  odometer_km: 24_534,
  fuel_type: 'gasoline',
  condition_grade: 4,
  condition_report: 'Very clean vehicle inside and out.',
  damage_notes: [],
  title_status: 'clean',
  province: 'Ontario',
  city: 'Mississauga',
  auction_start: '2026-04-05T19:00:00',
  starting_bid: 20_500,
  buy_now_price: null,
  images: ['https://placehold.co/800x600?text=CX-5'],
  selling_dealership: 'A dealer',
  lot: 'L-1',
  current_bid: null,
  bid_count: 0,
  auction_starts_at: NOW - 3_600_000,
  auction_ends_at: NOW + 3_600_000,
  auction_status: 'live',
  min_next_bid: 20_500,
  reserve_state: 'not-met',
  sold: false,
};

/** The first screen's reading as /api/pulse answers it. */
const PULSE = {
  usual_p95_ms_24h: 31,
  requests_24h: 1840,
  month_to_date: 72.46,
  forecast_month: 92.34,
  currency: 'USD',
  cost_read_at_ms: NOW - 60_000,
  uptime_seconds: 27_180,
  version: OWN.version,
  commit: OWN.commit,
  read_at_ms: NOW,
  people_all_time: 1284,
  people_new_by_day: [3, 0, 7],
  counted_from: '2026-10-24',
  visits_all_time: 2104,
  visits_from: '2026-09-02',
  visits_by_day: [61, 70, 58],
};

/** One published post as /api/blog/posts/{slug} answers it, with two slides. */
const POST = {
  slug: 'a-post-with-a-hook',
  title: 'A post with a hook',
  summary: 'The summary its hook draws, with 2 numbers in it.',
  body: 'The text.',
  body_html: '<p>The text.</p>\n',
  published_at: '2026-11-18T11:38:00Z',
  linkedin_url: 'https://www.linkedin.com/posts/a-post-with-a-hook',
  code_url: null,
  images: [1, 2].map((position) => ({
    position,
    url: `/api/blog/images/slide-${position}.png`,
    thumb_url: `/api/blog/images/slide-${position}.png`,
    alt_text: `Slide ${position}`,
    width: 1080,
    height: 1350,
  })),
  tags: [],
  topics: [],
  keywords: [],
  reading_minutes: 1,
  reads: 0,
  reads7d: 0,
  older: null,
  newer: null,
  related: [],
};

/** The list's first page as /api/blog/posts answers it: the one post as a card. */
const LIST = {
  cards: [
    {
      ...POST,
      published_at: '2026-11-18T11:38:00Z',
      cover: null,
      slides: 2,
      reading_minutes: 3,
      topics: [],
      keywords: [],
      reads: 0,
      reads7d: 0,
      matched: [],
    },
  ],
  total: 1,
  topics: [],
  keywords: [],
  months: [],
  next: null,
  terms: [],
};

/** Every request the renderer made, with the headers it sent: what the service reaches, and with what. */
type Asked = { url: string; headers: Record<string, string> };

/** A stand-in for the API at one origin, answering the reads the renderer makes, and keeping each one. */
/** The gate's results file added up, as /api/tests/summary answers it: 700 + 2,000 + 10 + 300 counted once. */
const SUMMARY = {
  version: OWN.version,
  ran_at: '2026-11-19T17:00:00Z',
  gate_seconds: 900,
  passed: 5010,
  failed: 0,
  skipped: 0,
  suites: [
    { id: 'vitest', name: 'Vitest', passed: 700, failed: 0, skipped: 0, seconds: 30 },
    { id: 'xunit-sqlite', name: 'xUnit', passed: 2000, failed: 0, skipped: 0, seconds: 300 },
    { id: 'xunit-live', name: 'live', passed: 10, failed: 0, skipped: 0, seconds: 10 },
    { id: 'xunit-cosmos', name: 'Cosmos', passed: 2000, failed: 0, skipped: 0, seconds: 300 },
    { id: 'browser-sqlite', name: 'Browser', passed: 300, failed: 0, skipped: 0, seconds: 600 },
  ],
};

function fakeApi(build = OWN, broken: string[] = []) {
  const asked: Asked[] = [];
  const read = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    asked.push({ url, headers: { ...(init?.headers as Record<string, string>) } });
    const path = url.slice(API.length).split('?')[0];
    if (!url.startsWith(API) || broken.some((part) => path.startsWith(part))) {
      throw new TypeError('fetch failed');
    }
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
    if (path === '/') return new Response(PAGE, { status: 200 });
    if (path === '/api/version') return json(build);
    if (path === '/api/vehicles') return json({ total: 1, vehicles: [vehicle] });
    if (path === '/api/facets')
      return json({ makes: [], body_styles: [], title_statuses: [], provinces: [] });
    if (path === `/api/vehicles/${vehicle.id}`) return json(vehicle);
    if (path === '/api/pulse') return json(PULSE);
    if (path === '/api/recommendations') return json(recommendations);
    if (path === '/api/tests/summary') return json(SUMMARY);
    if (path === `/api/blog/posts/${POST.slug}`) return json(POST);
    if (path === '/api/blog/posts') return json(LIST);
    if (path === '/api/auth/me')
      return json({ signed_in: true, email: 'reader@example.com', member_since_ms: NOW });
    if (path.startsWith('/api/docs/'))
      return new Response('# Doc\n\n## In plain words\n\nDrawn here.\n');
    return new Response('{}', { status: 404 });
  }) as typeof fetch;
  return { read, asked };
}

async function page(search: string, api = fakeApi(), cookie = '') {
  const render = createRenderer(OWN, 2_500, api.read);
  const headers = new Headers(cookie ? { cookie, 'x-other': 'not forwarded' } : {});
  const response = await render(`https://render.example/${search}`, headers, API);
  return { response, html: await response.text(), asked: api.asked };
}
// #endregion a-fake-api

// #region the-renderer
describe('the rendering service', () => {
  it('cuts the page the API serves at #root, by the marks index.html carries', () => {
    const cut = cutAtRoot(PAGE);
    expect(cut?.before.endsWith(ROOT_START)).toBe(true);
    expect(cut?.after.startsWith(ROOT_END)).toBe(true);
    expect(cutAtRoot('<div id="root"></div>')).toBeNull();
  });

  it('draws the page into #root with the first load beside it, the page around it as the API serves it', async () => {
    const { response, html } = await page('?view=inventory');
    expect(response.status).toBe(200);
    expect(response.headers.get(RENDERED_HEADER)).toBe('drawn');
    expect(html.startsWith('<!doctype html>')).toBe(true);
    expect(html).toContain('<div id="root" data-drawn="server">');
    expect(html).toContain('id="yard-first-load"');
    expect(html).not.toContain('The landing page the build drew.');
    expect(html).toContain('For a reader without JavaScript.');
    expect(html).toContain(String(vehicle.vin));
  });

  it('says in Server-Timing how long the page itself took to read, by name and milliseconds only (ADR: A rendering service beside the API)', async () => {
    const { response } = await page('?view=blog');
    expect(response.headers.get('Server-Timing')).toMatch(/^shell;dur=\d+(\.\d)?$/);
  });

  it("gives the review route a link preview of its own, and leaves the bare address with the build's (ADR: The public face)", async () => {
    const review = (await page('?view=review')).html;
    expect(review).toContain(
      '<title>Production .NET that stays up. Built with AI, measured on every version. | Steven Stout</title>'
    );
    const bare = (await page('')).html;
    expect(bare).not.toContain('Production .NET that stays up. Built with AI');
  });

  it('draws every post title on the blog list as a link to its own address, so a crawler reading the HTML finds each post (ADR: Search engines are told about every post)', async () => {
    const api = fakeApi();
    const older = { ...LIST.cards[0], slug: 'an-older-post', title: 'An older post' };
    const read = (async (input: RequestInfo | URL, init?: RequestInit) =>
      String(input).split('?')[0] === `${API}/api/blog/posts`
        ? new Response(JSON.stringify({ ...LIST, cards: [...LIST.cards, older], total: 2 }))
        : api.read(input, init)) as typeof fetch;
    const { html } = await page('?view=blog', { read, asked: api.asked });
    const drawn = html.slice(html.indexOf('<div id="root"'), html.indexOf('id="yard-first-load"'));
    expect(drawn.match(/data-mock="post-title-link"/g)).toHaveLength(2);
    for (const slug of [POST.slug, older.slug]) {
      expect(drawn).toContain(`href="/?view=blog&amp;post=${slug}"`);
    }
  });

  it('reads a named post beside the list, so a slow list never leaves the post out of the page', async () => {
    const api = fakeApi();
    const postPath = `${API}/api/blog/posts/${POST.slug}`;
    // The list answers only once the post has been asked for by both the head and the first load, or after the
    // page's deadline: read one after the other, the post would start with the deadline already spent.
    const slowList = (async (input: RequestInfo | URL, init?: RequestInit) => {
      if (
        String(input).startsWith(`${API}/api/blog/posts?`) ||
        String(input) === `${API}/api/blog/posts`
      ) {
        const start = Date.now();
        while (
          api.asked.filter((ask) => ask.url === postPath).length < 2 &&
          Date.now() - start < 3_000
        )
          await new Promise((resolve) => setTimeout(resolve, 10));
      }
      // As fetch does: a read whose signal has already gone is refused.
      if (init?.signal?.aborted) throw new DOMException('The read was aborted', 'AbortError');
      return api.read(input, init);
    }) as typeof fetch;
    const { html } = await page(`?view=blog&post=${POST.slug}`, {
      read: slowList,
      asked: api.asked,
    });
    expect(html).toContain('<p>The text.</p>');
  });

  it('draws a post address with its hook, the title, summary and first image, from the posts it read for the head (ADR-132)', async () => {
    const { html } = await page(`?view=blog&post=${POST.slug}`);
    // The hook's own box: the post itself is drawn whole below it now, its strip with every slide.
    const start = html.indexOf('data-hook="post"');
    const hook = html.slice(start, html.indexOf('</section>', start));
    expect(html.slice(start)).toContain('data-testid="blog-post"');
    expect(hook).toContain(POST.title);
    expect(hook).toContain(POST.summary);
    expect(hook).toContain('src="/api/blog/images/slide-1.png"');
    expect(hook).not.toContain('slide-2.png"');
    expect(postFor('view=blog&post=no-such-post', [POST])).toBeNull();
    expect(postFor('view=blog', [POST])).toBeNull();
  });

  it('sends the API page as it is while the API runs another build, so the browser draws it', async () => {
    const { response, html } = await page(
      '?view=inventory',
      fakeApi({ version: '1.0.3.99', commit: 'old' })
    );
    expect(response.headers.get(RENDERED_HEADER)).toBe('other-build 1.0.3.99');
    expect(html).toBe(PAGE);
  });

  it('draws the first screen with its figures on the bare address, so a crawler reads numbers and not the placeholder', async () => {
    const { html, asked } = await page('');
    expect(asked.some((read) => read.url.startsWith(`${API}/api/pulse`))).toBe(true);
    expect(html).toContain('"pulse":{');
    expect(html).toContain('$72.46');
    expect(html).not.toMatch(/>unread</);
    // A document's address draws no band, so it asks nothing of the pulse.
    const doc = await page('?doc=readme');
    expect(doc.asked.some((read) => read.url.startsWith(`${API}/api/pulse`))).toBe(false);
  });

  it('draws the review route whole on the server: the first screen from its two reads, the seven compact stops from their list and the close (ADR: The review route gives a reason before it asks)', async () => {
    const { response, html, asked } = await page('?view=review');
    expect(response.status).toBe(200);
    expect(response.headers.get(RENDERED_HEADER)).toBe('drawn');
    // The first screen: the claim, the live tile's figure read here so nothing moves, and the one quote.
    expect(html).toContain('Production .NET that stays up.');
    expect(html).toContain('3,010');
    const drew = recommendations.recommendations.find((entry) => entry.name === 'Drew Dell');
    expect(drew).toBeDefined();
    expect(html).toContain(escapeHtml(drew!.pull));
    expect(html).toContain(
      `Read all ${recommendations.recommendations.length} named recommendations`
    );
    expect(html).toContain('href="/?doc=quality"');
    // The tour, under it: seven compact cells, each a link to its stop's address with its one line, and no tall card.
    for (const stop of REVIEW_STOPS) expect(html).toContain(escapeHtml(stop.title));
    for (const stop of REVIEW_STOPS) expect(html).toContain(`href="${escapeHtml(stop.address)}"`);
    expect(html).toContain(escapeHtml(REVIEW_STOPS[0].line));
    expect(html).toContain('0 of 7 seen.');
    expect(html).not.toContain('Do this:');
    expect(html).not.toContain('Mark done');
    // The named voices under the questions, the pair and the close, drawn from the same read as the first screen's
    // quote, so nothing moves when the browser takes over.
    for (const name of [
      'Henry Crans',
      'Sarah Sebestyen',
      'Meagan Colley',
      'Sean Whitesell',
      'Khushbu Shah',
      'Anthony Fortner',
    ]) {
      const voice = recommendations.recommendations.find((entry) => entry.name === name);
      expect(voice, name).toBeDefined();
      expect(html).toContain(escapeHtml(voice!.pull));
    }
    // The ten-minute ask is below the first screen: the claim comes before it in the page.
    expect(html.indexOf('Production .NET that stays up.')).toBeLessThan(
      html.indexOf('Seven stops, ten minutes, all optional')
    );
    // The rail's row sits directly under Home.
    const home = html.indexOf('>Home<');
    const review = html.indexOf('>Check my work<');
    const blog = html.indexOf('>Blog<');
    expect(home).toBeGreaterThan(-1);
    expect(review).toBeGreaterThan(home);
    expect(blog).toBeGreaterThan(review);
    // The version, the recommendations, the tests summary and the pulse are read for it: no list, no document.
    const paths = asked.map((read) => read.url.slice(API.length).split('?')[0]);
    expect([...new Set(paths.filter((path) => path !== '/'))].sort()).toEqual([
      '/api/pulse',
      '/api/recommendations',
      '/api/tests/summary',
      '/api/version',
    ]);
    // 01 The work and the two live panels are drawn here with the pulse's figures, so nothing moves at the takeover
    // (sales1120): the first screen's panel and TheYard card's, its heading the tests tile's own figure.
    expect(html).toContain('data-mock="live-panel"');
    expect(html).toContain('31 ms');
    expect(html).toContain('$72.46');
    expect(html).toContain('3,010 tests passed on the version you are reading');
    expect(html).toContain('1,840');
    expect(html).toContain('id="case-conway"');
    expect(html).toContain('What has to work, where is it today, and what does success look like?');
  });

  it('draws the mirror whole on the server for ?for=ai, with its own preview, and the verdict for any other value', async () => {
    const { html } = await page('?view=review&for=ai');
    expect(html).toContain('Shipping with AI without losing quality.');
    expect(html).toContain('<title>Shipping with AI without losing quality | Steven Stout</title>');
    expect(html).toContain('3,010');
    expect(html).toContain('href="/?view=admin&amp;card=tests"');
    expect(html).not.toContain('Production .NET that stays up.');
    const other = (await page('?view=review&for=sales')).html;
    expect(other).toContain('Production .NET that stays up.');
  });

  it('draws the short path under the first screen on the server, nothing reached, its live tiles waiting for the browser', async () => {
    const { html, asked } = await page('?view=review');
    expect(html).toContain('0 of 3 opened.');
    for (const words of [
      'How do you keep quality when AI writes the code?',
      'What has to pass before anything ships?',
      'Is this a real system, running right now?',
      'See the slip and the fix',
      'See the test results',
      'Watch this lot live',
    ])
      expect(html).toContain(words);
    expect(html).toContain('href="/?view=admin&amp;card=tests"');
    // The path's tiles read in the browser, from their own kept answers; the server reads neither for them.
    const paths = asked.map((read) => read.url.slice(API.length).split('?')[0]);
    expect(paths.some((path) => path.startsWith('/api/review/'))).toBe(false);
    expect(paths.some((path) => path.startsWith('/api/vehicles'))).toBe(false);
    // In the page's order: the claim, the path, then the tour.
    const claim = html.indexOf('Production .NET that stays up.');
    const path = html.indexOf('0 of 3 opened.');
    const tour = html.indexOf('The full tour, if you want it');
    expect(claim).toBeLessThan(path);
    expect(path).toBeLessThan(tour);
  });

  it('names the mirror’s strip in its order with the API’s titles and the rest as a count, with no quote panel', async () => {
    const { html } = await page('?view=review&for=ai');
    const list = recommendations.recommendations;
    const order = [
      'Drew Dell',
      'Sean Whitesell',
      'Anthony Fortner',
      'Khushbu Shah',
      'Cliff True',
      'Henry Crans',
      'Meagan Colley',
    ];
    const named = order.filter((name) => list.some((entry) => entry.name === name));
    const places = named.map((name) => html.indexOf(`<strong>${escapeHtml(name)}</strong>`));
    expect(places.every((place) => place > -1)).toBe(true);
    expect([...places].sort((a, b) => a - b)).toEqual(places);
    for (const name of named) {
      const entry = list.find((each) => each.name === name)!;
      expect(html).toContain(escapeHtml(entry.title));
    }
    expect(html).toMatch(
      new RegExp(`and (<!-- -->)?${list.length - named.length}(<!-- -->)? more`)
    );
    expect(html).toContain(`${list.length} people who worked with him put their name on it`);
    // The strip names people; the verdict's quote panel is not drawn on the mirror.
    expect(html).not.toContain('data-testid="review-lead-quote"');
  });

  it('draws the home page’s first screen and its doors in the first load, the review route first, and no stack table', async () => {
    const { html } = await page('');
    // The first screen (ADR-132, the addendum on the split hook): the name line, the claim and the moves are in the
    // first load, so nothing moves when the browser takes over.
    expect(html).toContain('data-testid="landing-name"');
    expect(html).toContain('Built with AI. Gated so quality never slides.');
    expect(html).toContain('Check my work in 2 minutes');
    expect(html).toContain('or browse the live auction');
    // Start here: the review route, then the author, the inventory and the resume (ADR-082's addendum).
    const at = [
      'landing-banner-review',
      'landing-tile-author',
      'landing-tile-inventory',
      'landing-banner-resume',
    ].map((id) => html.indexOf(`data-testid="${id}"`));
    expect(at.every((index) => index > -1)).toBe(true);
    expect([...at].sort((a, b) => a - b)).toEqual(at);
    // The stack's table is on its own page; home carries its header and the way to it.
    expect(html).not.toContain('data-testid="landing-stack-table"');
    expect(html).toContain('href="/?view=stack"');
  });

  it('answers 503 when the API does not answer, the one case with no page to send', async () => {
    const { response } = await page('', fakeApi(OWN, ['/']));
    expect(response.status).toBe(503);
  });

  it('draws the page without the list when the list cannot be read, and the browser asks for it', async () => {
    const { response, html } = await page('?view=inventory', fakeApi(OWN, ['/api/vehicles']));
    expect(response.status).toBe(200);
    expect(html).toContain('Loading inventory');
    expect(html).toContain('"listing":null');
  });

  it('reads as the visitor: their cookie goes to the API and nothing else they sent does', async () => {
    const { html, asked } = await page('?view=inventory', fakeApi(), 'yard_session=abc');
    expect(html).toContain('reader@example.com');
    const me = asked.find((read) => read.url.endsWith('/api/auth/me'));
    expect(me?.headers.Cookie).toBe('yard_session=abc');
    for (const read of asked) {
      expect(Object.keys(read.headers).sort().join(',')).toMatch(/^(Cookie,)?User-Agent$/);
      expect(read.headers['User-Agent']).toBe('TheYard-SelfRead/1 (render)');
    }
  });

  it('names the site it draws for on every read, because one API serves both sites', async () => {
    const api = fakeApi();
    const render = createRenderer(OWN, 2_500, api.read);
    await (
      await render(
        'https://render.example/site/cosmos?view=inventory',
        new Headers(),
        API,
        'cosmos'
      )
    ).text();
    // The listing and its filters are the store's; the page itself is the same on both sites.
    const reads = api.asked.filter((read) => /\/api\/(vehicles|facets)/.test(read.url));
    expect(reads.length).toBe(2);
    for (const read of reads) expect(read.headers['X-Yard-Store']).toBe('cosmos');
  });

  it('lets the edge keep a page drawn for nobody, keyed by the view and the session cookie, and the browser still asks', async () => {
    const { response } = await page('?view=inventory');
    expect(response.headers.get('Netlify-CDN-Cache-Control')).toBe(EDGE_RULE);
    expect(response.headers.get('Netlify-Vary')).toBe(EDGE_VARY);
    expect(EDGE_VARY).toBe('query,cookie=theyard_session');
    expect(response.headers.get('Cache-Control')).toBe('no-cache');
    expect(response.headers.get('Vary')).toBe('Cookie');
  });

  it('never lets the edge keep a page drawn for a request that carried a cookie, whatever the cookie was', async () => {
    for (const cookie of ['theyard_session=abc', 'other=1']) {
      const { response } = await page('?view=inventory', fakeApi(), cookie);
      expect(response.headers.get('Netlify-CDN-Cache-Control')).toBe('private, no-store');
      expect(response.headers.get('Cache-Control')).toBe('private, no-cache');
      expect(response.headers.get('Netlify-Vary')).toBe(EDGE_VARY);
    }
  });

  it('writes a "<" in the first load as its escape, so nothing in the data can close the script early', async () => {
    const { html } = await page('?doc=readme');
    const script = html.slice(html.indexOf('id="yard-first-load"'));
    expect(script.slice(0, script.indexOf('</script>'))).not.toContain('<');
  });
});
// #endregion the-renderer

// #region api-only
describe('the rendering service reaches the API only', () => {
  it('asks the API origin it was given, and nothing else, for every view', async () => {
    for (const search of [
      '',
      '?view=inventory',
      `?vehicle=${vehicle.id}`,
      '?doc=author',
      '?view=admin',
    ]) {
      const { asked } = await page(search, fakeApi(), 'yard_session=abc');
      for (const read of asked) expect(read.url.startsWith(`${API}/`)).toBe(true);
    }
  });

  it('names no store, no connection string and no Azure SDK, and opens no socket of its own', () => {
    for (const source of [loadersSource, pageSource, renderSource, serverSource]) {
      expect(source).not.toMatch(
        /cosmos\.|@azure\/|mssql|tedious|ConnectionStrings|AccountEndpoint|createConnection|node:tls/i
      );
    }
    // Only the door opens a connection, and only to the site it stands in for in the browser suite.
    expect(serverSource).toMatch(/fetch\(`\$\{passOrigin\}/);
  });
});
// #endregion api-only
