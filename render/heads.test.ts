import { describe, expect, it } from 'vitest';
import indexHtml from '../index.html?raw';
import { headFor, withHead, SITE } from './heads';

const POSTS = [
  {
    slug: 'a-post',
    title: 'A post with a title',
    summary: 'What the post found, in one line.',
  },
];

/** The value of one tag in the page's head, read the way an unfurler reads it. */
function meta(html: string, pattern: RegExp): string | null {
  return html.match(pattern)?.[1] ?? null;
}

const read = {
  title: /<title>([^<]*)<\/title>/,
  description: /<meta\s+name="description"\s+content="([^"]*)"/,
  canonical: /<link rel="canonical" href="([^"]*)"/,
  ogUrl: /<meta property="og:url" content="([^"]*)"/,
  ogTitle: /<meta property="og:title" content="([^"]*)"/,
  ogDescription: /<meta\s+property="og:description"\s+content="([^"]*)"/,
  twitterTitle: /<meta name="twitter:title" content="([^"]*)"/,
  twitterDescription: /<meta\s+name="twitter:description"\s+content="([^"]*)"/,
};

describe('a link preview says what the page is (ADR-053, the addendum of 1.0.5.45)', () => {
  it.each([
    [
      '?view=review',
      'Production .NET that stays up. Built with AI, measured on every version.',
      '/?view=review',
    ],
    ['?view=review&for=ai', 'Shipping with AI without losing quality', '/?view=review&for=ai'],
    ['?view=review&for=nobody', 'Production .NET that stays up.', '/?view=review'],
    ['?view=blog', 'Blog', '/?view=blog'],
    ['?view=blog&post=a-post', 'A post with a title', '/?view=blog&post=a-post'],
    ['?view=about', 'About Steven', '/?view=about'],
    ['?doc=author', 'About Steven', '/?view=about'],
    ['?doc=quality', 'Quality you can watch', '/?doc=quality'],
    ['', 'Built with AI. Gated so quality never slides.', '/'],
    ['?r=abc123', 'Built with AI. Gated so quality never slides.', '/'],
    ['?view=recommendations', '13 people who worked with Steven', '/?view=recommendations'],
    [
      '?view=recommendations&r=abc123',
      '13 people who worked with Steven',
      '/?view=recommendations',
    ],
  ])(
    '%s gets its own title, description and address in every tag an unfurler reads',
    (search, title, path) => {
      const head = headFor(search, POSTS, 13);
      expect(head).not.toBeNull();
      const html = withHead(indexHtml, head!);
      expect(meta(html, read.title)).toContain(title);
      expect(meta(html, read.ogTitle)).toContain(title);
      expect(meta(html, read.twitterTitle)).toContain(title);
      expect(meta(html, read.canonical)).toBe(SITE + path.replace(/&/g, '&amp;'));
      expect(meta(html, read.ogUrl)).toBe(SITE + path.replace(/&/g, '&amp;'));
      const description = meta(html, read.description)!;
      expect(description.length).toBeLessThanOrEqual(160);
      expect(description).not.toContain('auction platform');
      expect(meta(html, read.ogDescription)).toBe(description);
      expect(meta(html, read.twitterDescription)).toBe(description);
    }
  );

  it('leaves every other address, and a post nobody published, with the head the build wrote', () => {
    expect(headFor('?view=inventory', POSTS, 13)).toBeNull();
    expect(headFor('?view=blog&post=no-such-post', POSTS, 13)?.title).toBe(
      headFor('?view=blog', POSTS, 13)?.title
    );
  });

  it('titles the recommendations with the count it is handed, never a typed one', () => {
    expect(headFor('?view=recommendations', POSTS, 14)?.title).toBe(
      '14 people who worked with Steven'
    );
  });

  it("writes home's and the recommendations' descriptions in the pages' own words", () => {
    expect(headFor('', POSTS, 13)?.description).toBe(
      'Lead / Staff .NET engineer, remote. One engineer, an AI agent and a gate that checks quality on every release. Every result public, the worst days included.'
    );
    expect(headFor('?view=recommendations', POSTS, 13)?.description).toMatch(
      /^In their own words, from LinkedIn\. /
    );
  });

  it('writes a post title that holds a quote or an angle bracket as text', () => {
    const html = withHead(
      indexHtml,
      headFor('?view=blog&post=x', [{ slug: 'x', title: 'A "quoted" <b>', summary: 's' }], 13)!
    );
    expect(html).toContain('A &quot;quoted&quot; &lt;b&gt;');
    expect(html).not.toContain('<b>');
  });

  it('never names the people the site is written for', () => {
    for (const search of [
      '',
      '?view=review',
      '?view=blog',
      '?view=about',
      '?view=recommendations',
      '?doc=quality',
    ]) {
      const head = JSON.stringify(headFor(search, POSTS, 13));
      expect(head).not.toMatch(/hiring manager|recruiter/i);
    }
  });
});
