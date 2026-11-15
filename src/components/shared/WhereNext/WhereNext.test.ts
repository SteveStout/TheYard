import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { NEXT_PAGES, nextFor } from '../../../lib/pageHooks';
import { WhereNext } from './WhereNext';

// The React side of the "Where next" footer (ladder v7): the same list the API draws on /about and the resume door
// (WhereNextTests.cs holds that side). Every page that carries one has its entry, and the footer draws exactly the
// file's links, each with its reason and its minutes.
describe('the Where next footer', () => {
  it('has an entry for every page that carries one', () => {
    for (const page of NEXT_PAGES) expect(nextFor(page).length, page).toBeGreaterThan(0);
  });

  it('draws the file’s links in order, each with its reason and minutes', () => {
    for (const page of NEXT_PAGES) {
      const html = renderToStaticMarkup(createElement(WhereNext, { page }));
      const hrefs = [...html.matchAll(/href="([^"]+)"/g)].map((match) =>
        match[1].replace(/&amp;/g, '&')
      );
      expect(hrefs, page).toEqual(nextFor(page).map((link) => link.href));
      for (const link of nextFor(page)) expect(html).toContain(`${link.minutes} min`);
    }
  });
});
