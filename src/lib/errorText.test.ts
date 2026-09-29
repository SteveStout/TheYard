import { describe, expect, it } from 'vitest';
import { messageParts } from './errorText';

/**
 * The errors card's message column (ADR: Error handling, one shape everywhere):
 * an address or a path in a browser error is marked as an identifier, so it may
 * break at any character and the table never scrolls sideways; the words
 * around it stay words, and nothing in the message is lost or reordered.
 */
describe('messageParts', () => {
  it('marks a module address as an identifier and keeps the words as words', () => {
    const parts = messageParts(
      'browser: Failed to fetch dynamically imported module: http://localhost:5173/src/components/admin/TrafficCard/TrafficCard.tsx'
    );
    expect(parts).toEqual([
      { text: 'browser: Failed to fetch dynamically imported module: ', identifier: false },
      {
        text: 'http://localhost:5173/src/components/admin/TrafficCard/TrafficCard.tsx',
        identifier: true,
      },
    ]);
  });

  it('marks a file path and a long unbroken run, and leaves a short word alone', () => {
    const parts = messageParts(
      'at C:\\Users\\dev\\TheYard\\src\\main.tsx then InvalidOperationExceptionWithAVeryLongName ok'
    );
    expect(parts.filter((part) => part.identifier).map((part) => part.text)).toEqual([
      'C:\\Users\\dev\\TheYard\\src\\main.tsx',
      'InvalidOperationExceptionWithAVeryLongName',
    ]);
    expect(parts.map((part) => part.text).join('')).toBe(
      'at C:\\Users\\dev\\TheYard\\src\\main.tsx then InvalidOperationExceptionWithAVeryLongName ok'
    );
  });

  it('is one plain part for an ordinary sentence', () => {
    expect(messageParts('server error response')).toEqual([
      { text: 'server error response', identifier: false },
    ]);
  });
});
