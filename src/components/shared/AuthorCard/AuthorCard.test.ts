import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { AUTHOR_CARD, AUTHOR_PHOTO } from '../../../lib/authorCard';
import { AuthorCard } from './AuthorCard';

// The author card the blog's side opens with, on the list and every post (ADR-142, the addendum on the landing views).
describe('the author card', () => {
  const html = renderToStaticMarkup(createElement(AuthorCard));

  it("shows his photo, his name and role, and About's own sentence", () => {
    expect(html).toContain(`src="${AUTHOR_PHOTO}"`);
    expect(html).toContain('Steven Stout');
    expect(html).toContain('Lead / Staff .NET engineer · Remote');
    expect(AUTHOR_CARD.line).toBe(
      'I have spent twelve years building full stack software in C#, SQL Server, React and Azure.'
    );
    expect(html).toContain(AUTHOR_CARD.line);
  });

  it('opens About Steven and the review route', () => {
    expect([...html.matchAll(/<a [^>]*href="([^"]+)"/g)].map((match) => match[1])).toEqual([
      '/?view=about',
      '/?view=review',
    ]);
  });
});
