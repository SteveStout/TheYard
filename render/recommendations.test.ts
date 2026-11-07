import { describe, expect, it } from 'vitest';
import data from '../data/recommendations.json';
import { createRenderer, RENDERED_HEADER } from './render';
import { ROOT_END, ROOT_START } from './page';

/** Text as React writes it into HTML: the five characters an attribute or a text node escapes. */
const escapeHtml = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, '&#x27;');

const API = 'https://api.example';
const OWN = { version: '1.0.3.100', commit: 'abc1234' };
const PAGE = `<!doctype html><html><head><title>TheYard</title></head><body>
    ${ROOT_START}<div id="root"></div>${ROOT_END}</body></html>`;

/** /api/recommendations as the API answers it: the file's entries and the facts beside them, the count from the file. */
const ANSWER = {
  source: data.source,
  recommendations: data.recommendations,
  count: data.recommendations.length,
  from: '2023',
  to: '2026',
  groups: [
    {
      key: 'team',
      title: 'People on his team',
      lead: 'The people who sat beside him.',
      names: data.recommendations.map((entry) => entry.name),
    },
  ],
};

describe('the recommendations page, drawn by the rendering service (ADR: The recommendations have a page)', () => {
  it('draws the page whole from its one read: the count from the file in the title, every name, nothing else read', async () => {
    const asked: string[] = [];
    const read = (async (input: RequestInfo | URL) => {
      const url = String(input);
      asked.push(url.slice(API.length).split('?')[0]);
      const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
      if (url === `${API}/`) return new Response(PAGE, { status: 200 });
      if (url.startsWith(`${API}/api/version`)) return json(OWN);
      if (url.startsWith(`${API}/api/recommendations`)) return json(ANSWER);
      return new Response('{}', { status: 404 });
    }) as typeof fetch;
    const render = createRenderer(OWN, 2_500, read);
    const response = await render(
      'https://render.example/?view=recommendations',
      new Headers(),
      API
    );
    const html = await response.text();

    expect(response.headers.get(RENDERED_HEADER)).toBe('drawn');
    expect(html).toContain(`${data.recommendations.length} people who worked with Steven`);
    for (const entry of data.recommendations) expect(html).toContain(escapeHtml(entry.name));
    expect(html).toContain('That is everyone');
    expect(asked.filter((path) => path !== '/' && path !== '/api/version')).toEqual([
      '/api/recommendations',
    ]);
  });
});
