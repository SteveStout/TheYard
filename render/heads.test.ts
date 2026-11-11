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
    ['?view=review', 'Check my work in ten minutes', '/?view=review'],
    ['?view=blog', 'Blog', '/?view=blog'],
    ['?view=blog&post=a-post', 'A post with a title', '/?view=blog&post=a-post'],
    ['?doc=author', 'About Steven', '/?doc=author'],
    ['?doc=quality', 'Quality you can watch', '/?doc=quality'],
  ])(
    '%s gets its own title, description and address in every tag an unfurler reads',
    (search, title, path) => {
      const head = headFor(search, POSTS);
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
    expect(headFor('', POSTS)).toBeNull();
    expect(headFor('?view=inventory', POSTS)).toBeNull();
    expect(headFor('?view=blog&post=no-such-post', POSTS)?.title).toBe(
      headFor('?view=blog', POSTS)?.title
    );
  });

  it('writes a post title that holds a quote or an angle bracket as text', () => {
    const html = withHead(
      indexHtml,
      headFor('?view=blog&post=x', [{ slug: 'x', title: 'A "quoted" <b>', summary: 's' }])!
    );
    expect(html).toContain('A &quot;quoted&quot; &lt;b&gt;');
    expect(html).not.toContain('<b>');
  });

  it('never names the people the site is written for', () => {
    for (const search of ['?view=review', '?view=blog', '?doc=author', '?doc=quality']) {
      const head = JSON.stringify(headFor(search, POSTS));
      expect(head).not.toMatch(/hiring manager|recruiter/i);
    }
  });
});
