import { describe, expect, it } from 'vitest';
import authorMarkdown from '../docs/author/AUTHOR.md?raw';
import data from '../data/recommendations.json';
import { createRenderer, RENDERED_HEADER } from './render';
import { ROOT_END, ROOT_START } from './page';

const API = 'https://api.example';
const OWN = { version: '1.0.3.100', commit: 'abc1234' };
const PAGE = `<!doctype html><html><head><title>TheYard</title></head><body>
    ${ROOT_START}<div id="root"></div>${ROOT_END}</body></html>`;

/** /api/recommendations as the API answers it, every entry from the file. */
const ANSWER = {
  source: data.source,
  recommendations: data.recommendations,
  count: data.recommendations.length,
  from: '2023',
  to: '2026',
  groups: [],
};

/** /api/blog/posts as the API answers it: one card, the newest post Why I write links to. */
const POSTS = {
  cards: [
    {
      slug: 'the-newest-post',
      title: 'The newest post',
      summary: 'Its summary.',
      published_at: '2026-11-27T14:25:57Z',
      cover: { url: '/c.png', thumb_url: '/c-320.png', width: 1080, height: 1350 },
      slides: 9,
      reading_minutes: 7,
      topics: [],
      keywords: [],
      reads: 1,
      reads7d: 1,
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

/** The drawn page as a reader sees its words: no tags, the five escapes undone, one space between words. */
const wordsOf = (html: string) =>
  html
    .replace(/<[^>]+>/g, ' ')
    .replace(/&quot;/g, '"')
    .replace(/&#x27;|&#39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ')
    .replace(/ ([,.;:!?])/g, '$1');

/** A paragraph of the document as words: its links' text kept, its bold marks gone. */
const plain = (line: string) =>
  line
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\*\*/g, '')
    .replace(/\s+/g, ' ')
    .replace(/ ([,.;:!?])/g, '$1')
    .trim();

/** The document's own paragraphs and third-level headings under a second-level heading, as words. */
function linesUnder(heading: string): string[] {
  const start = authorMarkdown.indexOf(`## ${heading}`);
  const end = authorMarkdown.indexOf('\n## ', start + 1);
  const section = authorMarkdown.slice(start, end === -1 ? undefined : end).split(/\n---\n/)[0];
  return section
    .split(/\n\s*\n/)
    .map((block) => block.trim())
    .filter((block) => block !== '' && !/^(## |!\[|- \[)/.test(block))
    .map((block) => plain(block.replace(/^(### |> )/, '')));
}

async function drawAbout(search: string) {
  const asked: string[] = [];
  const read = (async (input: RequestInfo | URL) => {
    const url = String(input);
    asked.push(url.slice(API.length).split('?')[0]);
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });
    if (url === `${API}/`) return new Response(PAGE, { status: 200 });
    if (url.startsWith(`${API}/api/version`)) return json(OWN);
    if (url.startsWith(`${API}/api/docs/author`)) return new Response(authorMarkdown);
    if (url.startsWith(`${API}/api/recommendations`)) return json(ANSWER);
    if (url.startsWith(`${API}/api/tests/summary`)) return json({ suites: [] });
    if (url.startsWith(`${API}/api/blog/posts`)) return json(POSTS);
    return new Response('{}', { status: 404 });
  }) as typeof fetch;
  const response = await createRenderer(OWN, 2_500, read)(
    `https://render.example/${search}`,
    new Headers(),
    API
  );
  return { response, html: await response.text(), asked };
}

describe('About Steven, drawn whole by the rendering service (ADR: The sidebar, the addendums on About Steven as a page and on the person beside the work)', () => {
  it('draws the page from its four reads, with no window anywhere', async () => {
    const { response, html, asked } = await drawAbout('?view=about');
    expect(response.headers.get(RENDERED_HEADER)).toBe('drawn');
    expect(html).toContain('data-mock="about-page"');
    expect(html).not.toMatch(/<dialog[^>]* open=""/);
    expect(asked.filter((path) => path !== '/' && path !== '/api/version').sort()).toEqual([
      '/api/blog/posts',
      '/api/docs/author',
      '/api/recommendations',
      '/api/tests/summary',
    ]);
  });

  it('draws the hero, How I lead and every Away block word for word, and neither the colleagues column nor the document close', async () => {
    const drawn = await drawAbout('?view=about');
    // The page as drawn, not the first load written beside it for the browser to start from.
    const html = drawn.html.slice(
      drawn.html.indexOf('data-mock="about-page"'),
      drawn.html.indexOf('</main>')
    );
    const words = wordsOf(html);
    for (const heading of ["Hi, I'm Steve.", 'How I lead', 'Away from the keyboard', 'Why I write'])
      for (const line of linesUnder(heading)) expect(words).toContain(line);
    // Three lead cards and the coaching card, seven personal rows and the strip: three stories sit inside their lead
    // cards (aboutWeave.ts), and every personal row carries the away panel's heading as its label.
    expect(html.match(/data-mock="lead-card"/g)).toHaveLength(3);
    expect(html.match(/data-mock="coaching"/g)).toHaveLength(1);
    expect(html.match(/data-mock="away-card"/g)).toHaveLength(7);
    expect(html.match(/data-mock="away-label"/g)).toHaveLength(7);
    expect(html.match(/data-mock="away-strip"/g)).toHaveLength(1);
    expect(html.match(/data-mock="aside"/g)).toHaveLength(3);
    // Why I write is never an empty box: the server's first draw carries the newest post.
    expect(html).toContain('href="/?view=blog&amp;post=the-newest-post"');
    expect(html).toContain('data-mock="intro-personal"');
    expect(html).not.toContain('author-side');
    expect(html).not.toContain('author-close');
    expect(html).not.toContain('Read my resume');
  });

  it('places the parts in the order a hiring manager reads them, the one ask last', async () => {
    const { html } = await drawAbout('?view=about');
    const at = (hook: string) => html.indexOf(`data-mock="${hook}"`);
    // The weave (aboutOrder.ts): the head, How I lead, a personal row, the work, the voices in two runs, the coaching
    // card, Why I write, the couple's photographs, the close.
    const order = [
      'eyebrow',
      'role-line',
      'lead-grid',
      'away-card',
      'proof',
      'voices',
      'voices-more',
      'coaching',
      'why-write',
      'away-strip',
      'close',
    ].map(at);
    expect(order.every((place) => place > -1)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
  });

  it('lands the window’s old address, ?doc=author, on the page', async () => {
    const { html } = await drawAbout('?doc=author&r=abc123');
    expect(html).toContain('data-mock="about-page"');
    expect(html).not.toMatch(/<dialog[^>]* open=""/);
  });
});
