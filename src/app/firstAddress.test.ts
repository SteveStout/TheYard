import { describe, expect, it } from 'vitest';
import { readFirstAddress } from './firstAddress';
import { pageOf } from './pageOpen';

describe('About Steven is a view of its own (ADR: The sidebar, the addendum on About Steven as a page)', () => {
  it('opens at ?view=about', () => {
    const first = readFirstAddress('?view=about');
    expect(first.about).toBe(true);
    expect(first.docKey).toBeNull();
  });

  it('lands the window’s old address, ?doc=author, on the view and opens no document', () => {
    const first = readFirstAddress('?doc=author&r=abc123');
    expect(first.about).toBe(true);
    expect(first.docKey).toBeNull();
    expect(new URLSearchParams('?doc=author&r=abc123').get('r')).toBe('abc123');
  });

  it('leaves every other document to its window', () => {
    const first = readFirstAddress('?doc=quality');
    expect(first.about).toBe(false);
    expect(first.docKey).toBe('quality');
  });

  it('is the page open when its flag is the only one, and gives way to the pages before it', () => {
    expect(pageOf(false, false, false, false, true)).toBe('about');
    expect(pageOf(false, false, true, false, true)).toBe('recommendations');
    expect(pageOf(false, false, false, false, false)).toBeNull();
  });
});
