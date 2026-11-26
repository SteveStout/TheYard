import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  markLinkRows,
  thumbSrcSet,
  fetchBlogStats,
  fetchList,
  fetchPost,
  fetchPosts,
  fetchedFor,
  picturesLine,
  depthsPassed,
  isNew,
  mostRead,
  postDate,
  postLink,
  ALL_POSTS_ADDRESS,
  readReport,
  sendBlogReport,
  sourceShares,
  type BlogCard,
} from './blog';

const post = (slug: string, published_at: string, reads: number, reads7d: number): BlogCard => ({
  slug,
  title: slug,
  summary: '',
  published_at,
  cover: null,
  slides: 0,
  reading_minutes: 1,
  topics: [],
  keywords: [],
  reads,
  reads7d,
  matched: [],
});

describe('the blog on the page', () => {
  const now = Date.parse('2026-10-22T21:00:00Z');

  it('marks a post NEW for seven days after LinkedIn had it, and not before or after', () => {
    expect(isNew('2026-10-19T14:00:00Z', now)).toBe(true);
    expect(isNew('2026-10-15T21:00:01Z', now)).toBe(true);
    expect(isNew('2026-10-15T21:00:00Z', now)).toBe(false);
    expect(isNew('2026-10-23T09:00:00Z', now)).toBe(false);
    expect(isNew(undefined, now)).toBe(false);
    expect(isNew('not a date', now)).toBe(false);
  });

  it('addresses the blog and each post as the blog opens them, the slug encoded', () => {
    expect(ALL_POSTS_ADDRESS).toBe('/?view=blog');
    expect(postLink('quality-on-both-ends')).toBe('/?view=blog&post=quality-on-both-ends');
    expect(postLink('a b&c')).toBe('/?view=blog&post=a%20b%26c');
  });

  it('prints the date in Central time, so an evening post keeps its own day', () => {
    expect(postDate('2026-10-19T14:00:00Z')).toBe('Oct 19, 2026');
    expect(postDate('2026-10-20T02:30:00Z')).toBe('Oct 19, 2026');
  });

  it('gives each source a whole share of the reads, largest first, and nothing for no reads', () => {
    expect(
      sourceShares([
        { source: 'direct', reads: 7 },
        { source: 'linkedin', reads: 64 },
        { source: 'search', reads: 11 },
        { source: 'ref', reads: 18 },
      ])
    ).toEqual([
      { source: 'linkedin', label: 'LinkedIn', reads: 64, share: 64 },
      { source: 'ref', label: 'Tagged links', reads: 18, share: 18 },
      { source: 'search', label: 'Search', reads: 11, share: 11 },
      { source: 'direct', label: 'Direct', reads: 7, share: 7 },
    ]);
    expect(sourceShares([{ source: 'direct', reads: 0 }])).toEqual([]);
  });

  it('ranks the most read by this week, then all time, then the newest', () => {
    const ranked = mostRead([
      post('old-favourite', '2026-09-01T00:00:00Z', 90, 2),
      post('this-week', '2026-10-19T00:00:00Z', 41, 41),
      post('tie-newer', '2026-10-15T00:00:00Z', 17, 2),
      post('quiet', '2026-10-01T00:00:00Z', 1, 0),
    ]);
    expect(ranked.map((p) => p.slug)).toEqual(['this-week', 'old-favourite', 'tie-newer']);
  });

  it('reports each scroll depth once, as the reader passes it', () => {
    expect(depthsPassed(0.2, new Set())).toEqual([]);
    expect(depthsPassed(0.55, new Set())).toEqual([25, 50]);
    expect(depthsPassed(1, new Set([25, 50]))).toEqual([75, 100]);
  });

  it('reports a read with the tagged code and utm fields from the address, and no referrer when there was none', () => {
    expect(
      readReport(
        'solid-in-five-real-files',
        '?r=ab12cd&utm_source=linkedin',
        'https://www.linkedin.com/'
      )
    ).toEqual({
      slug: 'solid-in-five-real-files',
      referrer: 'https://www.linkedin.com/',
      ref: 'ab12cd',
      utm_source: 'linkedin',
      utm_medium: null,
      utm_campaign: null,
    });
    expect(readReport('a-post', '?r=NOTACODE', '').ref).toBeNull();
    expect(readReport('a-post', '', '').referrer).toBeNull();
  });
});

describe('the pictures line', () => {
  it('counts the kept pictures and their size, and says when the listing was not read', () => {
    expect(picturesLine({ count: 0, bytes: 0, source: 'memory' })).toBe('0 pictures kept, 0 KB.');
    expect(picturesLine({ count: 1, bytes: 212_000, source: 'the blog container' })).toBe(
      '1 picture kept, 212 KB.'
    );
    expect(picturesLine({ count: 9, bytes: 2_450_000, source: 'the blog container' })).toBe(
      '9 pictures kept, 2.5 MB.'
    );
    expect(picturesLine({ count: null, bytes: 0, source: 'the listing answered 403' })).toBe(
      'Pictures: not read (the listing answered 403).'
    );
  });
});

// The network is a stub here: each read answers what the test hands it, and the address asked for is kept.
describe('the blog read from the API', () => {
  afterEach(() => vi.unstubAllGlobals());

  const answering = (status: number, body: unknown = {}) => {
    const fetchMock = vi.fn(
      async (_url: RequestInfo | URL) => new Response(JSON.stringify(body), { status })
    );
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  };

  it('reads the list, a page of it, a post and the numbers from their own addresses', async () => {
    const fetchMock = answering(200, { posts: [] });
    expect(await fetchPosts()).toEqual({ posts: [] });
    await fetchList('/api/blog/posts?topic=ai');
    await fetchPost('a post/2');
    await fetchBlogStats();
    expect(fetchMock.mock.calls.map((call) => String(call[0]))).toEqual([
      '/api/blog/posts',
      '/api/blog/posts?topic=ai',
      '/api/blog/posts/a%20post%2F2',
      '/api/admin/blog',
    ]);
    expect(fetchedFor('a-post')).toBe('/api/blog/posts/a-post');
  });

  it('says no post for a 404, nothing to read for a 503, and fails on any other error', async () => {
    answering(404);
    expect(await fetchPost('no-such-post')).toBeNull();
    answering(503);
    expect(await fetchPosts()).toBeNull();
    answering(500);
    await expect(fetchList('/api/blog/posts')).rejects.toThrow('The blog API responded with 500');
  });

  it('sends a report as JSON that outlives the page, and a lost one costs nothing', async () => {
    const fetchMock = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => {
      throw new Error('offline');
    });
    vi.stubGlobal('fetch', fetchMock);
    sendBlogReport('/api/blog/events', { slug: 'a-post', type: 'click', value: 'slides' });
    await Promise.resolve();
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe('/api/blog/events');
    expect(init).toMatchObject({ method: 'POST', keepalive: true });
    expect(JSON.parse(String(init?.body))).toEqual({
      slug: 'a-post',
      type: 'click',
      value: 'slides',
    });
  });
});

describe('a slide thumbnail offers its sharper cuts', () => {
  it('names the 320, 480 and 640 px cuts beside the one the API names', () => {
    expect(thumbSrcSet('/blog/images/0123456789abcdef-320.webp')).toBe(
      '/blog/images/0123456789abcdef-320.webp 320w, /blog/images/0123456789abcdef-480.webp 480w, /blog/images/0123456789abcdef-640.webp 640w'
    );
  });

  it('offers nothing for an address that is not a 320 px thumbnail', () => {
    expect(thumbSrcSet('/blog/images/0123456789abcdef.webp')).toBe('');
  });
});

describe("a post's link-only list items are marked for a thumb's height", () => {
  it('marks an item that is only a link and leaves a link inside a sentence alone', () => {
    const html =
      '<ul><li><a href="https://a.example" target="_blank" rel="noreferrer">A page</a></li>' +
      '<li>Read <a href="https://b.example">the post</a> first</li><li>No link</li></ul>';
    expect(markLinkRows(html)).toBe(
      '<ul><li data-link-row=""><a href="https://a.example" target="_blank" rel="noreferrer">A page</a></li>' +
        '<li>Read <a href="https://b.example">the post</a> first</li><li>No link</li></ul>'
    );
  });

  it('leaves an item holding two links unmarked', () => {
    const html = '<li><a href="https://a.example">A</a> and <a href="https://b.example">B</a></li>';
    expect(markLinkRows(html)).toBe(html);
  });
});
