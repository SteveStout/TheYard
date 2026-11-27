import { describe, expect, it } from 'vitest';
import { BLOG_LEDE, listHead, postHead } from './blogHead';

// The blog's page head (ADR-142, the addendum on the landing views): a post's own title is the page's one h1, for any
// post, not only the one the design was drawn on.
describe("the blog's page head", () => {
  it("makes any post's own title the h1, its summary the line and its first slide the cover", () => {
    const image = {
      position: 1,
      url: '/a.png',
      thumb_url: '/a-320.png',
      alt_text: 'A',
      width: 10,
      height: 12,
    };
    for (const title of [
      'Every new client could have needed its own database. None did.',
      'A post',
      'Blog notes',
    ])
      expect(postHead({ title, summary: 'Its line.', image }).title).toBe(title);
    const head = postHead({
      title: 'A post',
      summary: 'Its line.',
      image,
      published_at: '2026-11-25T18:00:00Z',
      reading_minutes: 5,
    });
    expect(head.lede).toBe('Its line.');
    expect(head.cover).toBe(image);
    expect(head.eyebrow).toEqual(['Steven Stout', 'Blog', 'Nov 25, 2026 · 5 min read']);
  });

  it('leaves the date out of the eyebrow until the post is read, and says Blog with no post at all', () => {
    expect(postHead({ title: 'A post', summary: 'x', image: null }).eyebrow).toEqual([
      'Steven Stout',
      'Blog',
    ]);
    expect(postHead(null).title).toBe('Blog');
  });

  it("counts the list's posts in the eyebrow, read from the list and never typed", () => {
    expect(listHead(21)).toEqual({
      eyebrow: ['Steven Stout', 'Blog', '21 posts, newest first'],
      title: 'Blog',
      lede: BLOG_LEDE,
      cover: null,
    });
    expect(listHead(null).eyebrow).toEqual(['Steven Stout', 'Blog']);
  });
});
