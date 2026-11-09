import { describe, expect, it } from 'vitest';
import {
  COLUMN_ROOM_PX,
  NARROW_ROOM_PX,
  STACK_FROM_COLUMNS,
  addressParts,
  dottedParts,
  LONG_TOKEN,
  stacksAt,
} from './tableFit';

describe('when a table stacks', () => {
  it('stacks the store on a small desk and keeps it a table on a wide one', () => {
    // The store's nine columns (six of words; a time, a duration and a charge):
    // about 660 at 1024 on 1.0.3.29, cut at the edge.
    expect(stacksAt(660, 6, 3)).toBe(true);
    expect(stacksAt(6 * COLUMN_ROOM_PX + 3 * NARROW_ROOM_PX, 6, 3)).toBe(false);
  });

  it('stacks the log on a phone and keeps it a table on a tablet', () => {
    // A category and a message of words; a time and a level on one line each.
    expect(stacksAt(316, 2, 2)).toBe(true);
    expect(stacksAt(700, 2, 2)).toBe(false);
  });

  it('keeps a table of short numbers a table where they fit', () => {
    expect(stacksAt(662, 0, 6)).toBe(false);
  });

  it('never stacks two columns, however narrow', () => {
    expect(stacksAt(100, STACK_FROM_COLUMNS - 1)).toBe(false);
    expect(stacksAt(100, 1, 1)).toBe(false);
  });

  it('decides nothing on a box not laid out yet', () => {
    expect(stacksAt(0, 6, 3)).toBe(false);
  });
});

describe('a dotted name', () => {
  it('breaks after each dot and loses no character', () => {
    const name = 'TheYard.Infrastructure.Cosmos.CosmosStore';
    expect(dottedParts(name)).toEqual(['TheYard.', 'Infrastructure.', 'Cosmos.', 'CosmosStore']);
    expect(dottedParts(name).join('')).toBe(name);
  });

  it('keeps a name with no dot whole', () => {
    expect(dottedParts('Information')).toEqual(['Information']);
  });
});

describe('a long address in a sentence', () => {
  it('breaks after its own punctuation and leaves the words whole', () => {
    const line =
      'TypeError: Failed to fetch dynamically imported module: http://localhost:5173/src/components/admin/ErrorsCard/ErrorsCard.tsx';
    const parts = addressParts(line);
    expect(parts.join('')).toBe(line);
    expect(parts).toContain('Failed');
    expect(parts).toContain('ErrorsCard/');
    expect(Math.max(...parts.map((part) => part.length))).toBeLessThanOrEqual(LONG_TOKEN);
  });

  it('leaves a sentence of ordinary words as its words and spaces', () => {
    expect(addressParts("Executed DbCommand (0ms) [Parameters=[], CommandType='Text']")).toEqual([
      'Executed',
      ' ',
      'DbCommand',
      ' ',
      '(0ms)',
      ' ',
      '[Parameters=[],',
      ' ',
      "CommandType='Text']",
    ]);
  });
});
