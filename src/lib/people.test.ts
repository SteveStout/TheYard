import { describe, expect, it } from 'vitest';
import { countedFromWords, countWords, monthWords, peopleFrom } from './people';

const answer = {
  people_all_time: 1284,
  people_new_today: 23,
  people_new_month: 311,
  scanners_left_out: 4902,
  counted_from: '2026-10-24',
  people_new_by_day: [
    { day: '2026-10-23', people: 0 },
    { day: '2026-10-24', people: 23 },
  ],
  source: 'Cosmos DB visitors',
};

describe('the people count from /api/admin/visitors/people', () => {
  it('is read into the shape, every figure as it came, with its source', () => {
    expect(peopleFrom(answer)).toEqual(answer);
  });

  it('keeps a figure not counted yet as null, never as zero', () => {
    expect(peopleFrom({ ...answer, people_all_time: null })?.people_all_time).toBeNull();
  });

  it('is null when it is not that shape, so the card says so instead of drawing a number it does not have', () => {
    expect(peopleFrom(null)).toBeNull();
    expect(peopleFrom({ ...answer, people_all_time: -1 })).toBeNull();
    expect(peopleFrom({ ...answer, counted_from: 'Oct 24' })).toBeNull();
    expect(peopleFrom({ ...answer, people_new_by_day: [{ day: 'today', people: 1 }] })).toBeNull();
    const { source: _source, ...unsourced } = answer;
    expect(peopleFrom(unsourced)).toBeNull();
  });
});

describe('the words the card writes', () => {
  it('marks thousands, and says a count that has not begun in words', () => {
    expect(countWords(1284)).toBe('1,284');
    expect(countWords(null)).toBe('not counted yet');
  });

  it('writes the day the count began and the month a monthly figure covers', () => {
    expect(countedFromWords('2026-10-24')).toBe('24 Oct 2026');
    expect(monthWords(new Date('2026-10-24T12:00:00Z'))).toBe('October');
  });
});
