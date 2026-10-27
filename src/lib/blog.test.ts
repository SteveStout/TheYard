import { describe, expect, it } from 'vitest';
import {
  picturesLine,
  depthsPassed,
  isNew,
  mostRead,
  postDate,
  readReport,
  sourceShares,
  type BlogPost,
} from './blog';

const post = (slug: string, published_at: string, reads: number, reads7d: number): BlogPost => ({
  slug,
  title: slug,
  summary: '',
  body: '',
  published_at,
  linkedin_url: `https://www.linkedin.com/posts/${slug}`,
  code_url: null,
  images: [],
  tags: [],
  reads,
  reads7d,
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
