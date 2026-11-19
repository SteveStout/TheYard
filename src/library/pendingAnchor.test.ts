import { afterEach, describe, expect, it } from 'vitest';
import { landOnSection, pendingAnchor } from './pendingAnchor';

afterEach(() => landOnSection(''));

describe('the section the next document lands on (ADR: The review route gives a reason before it asks)', () => {
  it('holds a section a link asks for until it is landed on, and nothing once it is', () => {
    expect(pendingAnchor()).toBe('');
    landOnSection('the-slip-and-the-turn');
    expect(pendingAnchor()).toBe('the-slip-and-the-turn');
    // A second ask before the document is drawn wins: the last button pressed is where the reader is going.
    landOnSection('what-is-still-thin');
    expect(pendingAnchor()).toBe('what-is-still-thin');
    landOnSection('');
    expect(pendingAnchor()).toBe('');
  });
});
