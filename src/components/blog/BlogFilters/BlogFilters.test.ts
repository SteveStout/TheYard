import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { NO_FILTER } from '../../../lib/blogFilter';
import { BlogFilters } from './BlogFilters';

// The keywords fold behind one pill so the first post is on the first screen (ADR-142, the addendum on the landing
// views); the row stays in the page, hidden, so a crawler still reads every link.
describe("the blog's keywords", () => {
  const keywords = ['dotnet', 'azure', 'sql'].map((key) => ({ key, label: key, count: 1 }));
  const drawn = (keyword: string | null) =>
    renderToStaticMarkup(
      createElement(BlogFilters, {
        filter: { ...NO_FILTER, keyword },
        topics: [],
        keywords,
        months: [],
        showing: 3,
        of: 3,
        onChange: () => {},
      })
    );

  it('fold behind one pill that counts them, the row still in the page but hidden', () => {
    const html = drawn(null);
    expect(html).toMatch(/aria-expanded="false"[^>]*>More keywords \(3\)<\/button>/);
    expect(html).toMatch(/<div id="blog-keywords" hidden=""[^>]*data-mock="keywords-row"/);
    for (const keyword of keywords) expect(html).toContain(`>${keyword.label}<small>`);
  });

  it('stand open when the address already presses one of them', () => {
    const html = drawn('azure');
    expect(html).toContain('aria-expanded="true"');
    expect(html).not.toMatch(/id="blog-keywords" hidden=""/);
  });
});
