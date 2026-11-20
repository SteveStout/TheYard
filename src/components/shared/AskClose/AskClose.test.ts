import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import {
  BOOK_ADDRESS,
  CLOSE,
  LINKEDIN_ADDRESS,
  RESUME_ADDRESS,
  VERDICT,
} from '../../../lib/reviewVerdict';
import { AskClose } from './AskClose';

// The site's one closing row (ADR: The sales pages set the site's look, the addendum on the one close): a blog post,
// the recommendations page and the review route draw it from here, with the words reviewVerdict.ts holds; /about draws
// the same words in C# (AskCloseHtmlTests holds them equal).
const drawn = (props: Partial<Parameters<typeof AskClose>[0]> = {}) =>
  renderToStaticMarkup(createElement(AskClose, { onBook: () => {}, ...props }));

const moves = (html: string) =>
  [
    ...html.matchAll(
      /<a class="([^"]+)" href="([^"]+)"[^>]*data-action="([^"]+)"[^>]*>(.*?)<\/a>/g
    ),
  ].map(([, classes, href, action, inner]) => ({
    kind: classes.split(' ')[0],
    href: href.replace(/&amp;/g, '&'),
    action,
    words: inner.replace(/<svg.*<\/svg>/, ''),
  }));

describe('the closing row', () => {
  it('asks the calibrated question with its line, in a card the picture checks can find', () => {
    const html = drawn();
    expect(CLOSE.question).toBe(
      'What has to work, where is it today, and what does success look like?'
    );
    expect(CLOSE.line).toBe(
      'Book 20 minutes opens an email to me with those three questions in it, and I answer with a time.'
    );
    expect(html).toMatch(/^<section[^>]+data-mock="close"/);
    expect(html).toContain('class="op-ask" data-mock="close-ask"');
    expect(html).toContain(
      `<h2 id="ask-close-title" class="op-ask-q" data-mock="close-question">${CLOSE.question}</h2>`
    );
    expect(html).toContain(`<p class="op-ask-sub">${CLOSE.line}</p>`);
    expect(html).toContain('aria-labelledby="ask-close-title"');
    expect(html).not.toContain('op-num');
  });

  it('draws the three moves in order: Book the filled pill, the resume the gold one, LinkedIn the outlined one', () => {
    expect(moves(drawn())).toEqual([
      { kind: 'op-pill-primary', href: BOOK_ADDRESS, action: 'next.book', words: VERDICT.book },
      {
        kind: 'op-pill-resume',
        href: RESUME_ADDRESS,
        action: 'next.resume',
        words: VERDICT.resume,
      },
      {
        kind: 'op-pill-secondary',
        href: LINKEDIN_ADDRESS,
        action: 'next.linkedin',
        words: CLOSE.linkedin,
      },
    ]);
    expect(BOOK_ADDRESS).toBe('/api/go/book');
    expect(drawn()).toMatch(/data-mock="close-moves">/);
    expect(drawn()).toMatch(/op-pill-resume[^>]*><svg/);
  });

  it('carries the review route’s number badge, its own action names and what follows the moves', () => {
    const html = drawn({
      number: '06',
      action: 'review',
      id: 'talk',
      headingId: 'review-close-title',
      testId: 'review-close',
      after: createElement('button', { type: 'button' }, 'Send it'),
    });
    expect(html).toContain('id="talk"');
    expect(html).toContain('data-testid="review-close"');
    expect(html).toContain('data-testid="review-close-resume"');
    expect(html).toContain('<span class="op-num" data-mock="section-num">06</span>');
    expect(moves(html).map((move) => move.action)).toEqual([
      'review.book',
      'review.resume',
      'review.linkedin',
    ]);
    expect(html).toMatch(
      /Message me on LinkedIn<\/a><button type="button">Send it<\/button><\/div>/
    );
  });

  it('draws the home page’s form: Next step for a screen reader, the question as words under its 08, no step above', () => {
    const html = drawn({
      number: '08',
      heading: CLOSE.label,
      form: 'home',
      spaced: false,
      headingId: 'review-close-title',
    });
    expect(html).toContain('data-form="home"');
    expect(html).toMatch(/<h2 id="review-close-title" class="[^"]*srOnly[^"]*">Next step<\/h2>/);
    expect(html).toContain(
      `<p class="op-ask-q" data-mock="close-question"><span class="op-num" data-mock="section-num">08</span>${CLOSE.question}</p>`
    );
    expect(html).not.toMatch(/class="[^"]*spaced/);
    expect(drawn()).toMatch(/^<section class="[^"]*spaced/);
  });
});
