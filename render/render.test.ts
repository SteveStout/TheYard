import { describe, expect, it } from 'vitest';
import loadersSource from './loaders.ts?raw';
import pageSource from './page.ts?raw';
import renderSource from './render.ts?raw';
import serverSource from './server.mjs?raw';
import { cutAtRoot, ROOT_END, ROOT_START } from './page';
import { createRenderer, RENDERED_HEADER } from './render';

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

/** Every request the renderer made, with the headers it sent: what the service reaches, and with what. */
type Asked = { url: string; headers: Record<string, string> };

/** A stand-in for the API at one origin, answering the reads the renderer makes, and keeping each one. */
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

  it('sends the API page as it is while the API runs another build, so the browser draws it', async () => {
    const { response, html } = await page(
      '?view=inventory',
      fakeApi({ version: '1.0.3.99', commit: 'old' })
    );
    expect(response.headers.get(RENDERED_HEADER)).toBe('other-build 1.0.3.99');
    expect(html).toBe(PAGE);
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
