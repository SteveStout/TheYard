import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { WorkingLayers, returnFocus, swallow } from './WorkingLock';

/**
 * The lock as it draws (ADR: One working lock for the whole site, the addendum of 1.0.4.20): the
 * status line a screen reader hears is always in the page and says "Working" only while the lock is
 * up; the lock itself is a modal named Working and no live region; the turning dots are hidden from a
 * screen reader; focus goes back to the main landmark when the element that had it is gone; and a
 * click or a pointer under the lock goes nowhere. The store is played by the locked value itself.
 */
const draw = (locked: boolean) => renderToStaticMarkup(createElement(WorkingLayers, { locked }));

describe('the working lock', () => {
  it('keeps a quiet status line in the page and says Working in it only while locked', () => {
    expect(draw(false)).toMatch(/<span[^>]*role="status"[^>]*aria-live="polite"[^>]*><\/span>/);
    expect(draw(true)).toMatch(
      /<span[^>]*role="status"[^>]*aria-live="polite"[^>]*>Working<\/span>/
    );
  });

  it('is a modal named Working with no live region of its own', () => {
    const lock = draw(true).match(/<div[^>]*data-testid="working-lock"[^>]*>/)?.[0] ?? '';
    expect(lock).toContain('role="dialog"');
    expect(lock).toContain('aria-label="Working"');
    expect(lock).toContain('data-locked="true"');
    expect(lock).not.toContain('aria-live');
    expect(lock).not.toContain('role="status"');
  });

  it('hides the turning dots from a screen reader', () => {
    expect(draw(true)).toMatch(/<b>Working<span[^>]*aria-hidden="true"[^>]*><\/span><\/b>/);
  });

  it('hands its fade to the CSS, so the two cannot drift', () => {
    expect(draw(false)).toContain('--working-fade:180ms');
  });

  it('gives focus back where it was while that element is still in the page', () => {
    const before = { focus: vi.fn() };
    const main = { focus: vi.fn() };
    returnFocus(
      before as unknown as Element,
      {
        contains: () => true,
        querySelector: () => main,
      } as unknown as Document
    );
    expect(before.focus).toHaveBeenCalledOnce();
    expect(main.focus).not.toHaveBeenCalled();
  });

  it('sends focus to the main landmark when the answer replaced the element that had it', () => {
    const before = { focus: vi.fn() };
    const main = { focus: vi.fn() };
    const asked: string[] = [];
    returnFocus(
      before as unknown as Element,
      {
        contains: () => false,
        querySelector: (selector: string) => {
          asked.push(selector);
          return main;
        },
      } as unknown as Document
    );
    expect(before.focus).not.toHaveBeenCalled();
    expect(main.focus).toHaveBeenCalledOnce();
    expect(asked).toEqual(['main']);
  });

  it('swallows a click and a key while locked', () => {
    const event = { preventDefault: vi.fn(), stopPropagation: vi.fn() };
    swallow(event);
    expect(event.preventDefault).toHaveBeenCalledOnce();
    expect(event.stopPropagation).toHaveBeenCalledOnce();
  });
});
