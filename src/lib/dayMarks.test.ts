import { describe, expect, it } from 'vitest';
import { dayAxisLabels, dayMarks, weekendRuns } from './dayMarks';

// Mon 2026-10-05 to Sun 2026-10-18: two full weeks, as the activity card's days.
const days = Array.from({ length: 14 }, (_, offset) => ({
  at: new Date(Date.UTC(2026, 9, 5 + offset)).toISOString().slice(0, 10),
}));

describe('dayMarks', () => {
  it('names every slot by its UTC day of the week', () => {
    const marks = dayMarks(days, 14 * 24 * 60);
    expect(marks.map((mark) => mark.day).join(' ')).toBe(
      'Mon Tue Wed Thu Fri Sat Sun Mon Tue Wed Thu Fri Sat Sun'
    );
  });

  it('labels every Monday and Friday and nothing else', () => {
    const labelled = dayMarks(days, 14 * 24 * 60)
      .filter((mark) => mark.labelled)
      .map((mark) => `${mark.day} ${days[mark.index].at}`);
    expect(labelled).toEqual([
      'Mon 2026-10-05',
      'Fri 2026-10-09',
      'Mon 2026-10-12',
      'Fri 2026-10-16',
    ]);
  });

  it('shades Saturday and Sunday as one run each weekend', () => {
    expect(weekendRuns(dayMarks(days, 14 * 24 * 60))).toEqual([
      { from: 5, to: 6 },
      { from: 12, to: 13 },
    ]);
  });

  it('labels a day of four-hour buckets once, on its first bucket', () => {
    // Fri 2026-10-09, six four-hour buckets, then Sat.
    const buckets = Array.from({ length: 8 }, (_, step) => ({
      at: new Date(Date.UTC(2026, 9, 9, step * 4)).toISOString(),
    }));
    const marks = dayMarks(buckets, 30 * 24 * 60);
    expect(marks.filter((mark) => mark.labelled).map((mark) => mark.index)).toEqual([0]);
    expect(weekendRuns(marks)).toEqual([{ from: 6, to: 7 }]);
  });

  it('marks nothing on a window shorter than a day, and skips a time it cannot read', () => {
    expect(dayMarks(days, 60)).toEqual([]);
    expect(dayMarks([{ at: 'not a time' }, days[0]], 7 * 24 * 60)).toHaveLength(1);
  });
});

describe('dayAxisLabels', () => {
  it('writes the day and the date, with the month on the first label only', () => {
    const texts = dayAxisLabels(dayMarks(days, 14 * 24 * 60), days).map((label) => label.text);
    expect(texts).toEqual(['Mon 5 Oct', 'Fri 9', 'Mon 12', 'Fri 16']);
  });
});
