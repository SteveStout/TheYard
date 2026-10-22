import { describe, expect, it } from 'vitest';
import {
  angleOf,
  arcPath,
  cardToneWord,
  dayMarks,
  detailJoint,
  legendChips,
  linePath,
  newestValue,
  portalSentence,
  railGlance,
  ticks,
  toneWord,
  valueLines,
  xOf,
  yOf,
} from './cloud';

describe('the Cloud infrastructure frame', () => {
  it('fills the dial clockwise from twelve o’clock and never closes the ring', () => {
    expect(angleOf(0)).toBe(-90);
    expect(angleOf(25)).toBe(0);
    expect(angleOf(150)).toBeCloseTo(269.964, 2);
    expect(arcPath(100, 100, 50, 0, 75)).toContain(' 1 1 ');
    expect(ticks()).toHaveLength(40);
    expect(ticks().filter((tick) => tick.long)).toHaveLength(10);
  });

  it('breaks a line over a minute nobody read rather than drawing a zero', () => {
    expect(
      linePath([
        { x: 0, y: 10 },
        { x: 1, y: null },
        { x: 2, y: 20 },
      ])
    ).toBe('M 0.0 10.0 M 2.0 20.0');
    expect(yOf(100, 50)).toBe(0);
    expect(yOf(-5, 50)).toBe(50);
    expect(xOf(5, 0, 10, 100)).toBe(50);
  });

  it('names only Mondays and Fridays along the axis', () => {
    const start = Date.UTC(2026, 9, 12);
    const marks = dayMarks(start, start + 7 * 86_400_000);
    expect(marks.filter((mark) => mark.label !== null).map((mark) => mark.label)).toEqual([
      'Mon',
      'Fri',
      'Mon',
    ]);
  });

  it('says what a tone means in the owner’s words', () => {
    expect(toneWord('good')).toBe('fine');
    expect(toneWord('info')).toBe('for information');
    expect(toneWord('alarm')).toBe('warning');
  });

  it('prints a listed readout value one item a line, and keeps a thousands comma whole', () => {
    expect(
      valueLines('stevenstout.biz, theyard-cosmos.stevenstout.biz, www.stevenstout.biz')
    ).toEqual(['stevenstout.biz', 'theyard-cosmos.stevenstout.biz', 'www.stevenstout.biz']);
    expect(valueLines('2,152')).toEqual(['2,152']);
  });
});

describe('the CardShell slots', () => {
  it('splits a legend into one chip a series or mark', () => {
    expect(legendChips('teal CPU \u00b7 green memory \u00b7 dashed 80 %')).toEqual([
      'teal CPU',
      'green memory',
      'dashed 80 %',
    ]);
    expect(legendChips('')).toEqual([]);
  });

  it('prints a sparkline with its newest reading and none when nothing was read', () => {
    expect(newestValue([4, 5, null])).toBe(5);
    expect(newestValue([null, null])).toBeNull();
  });

  it('gives the rail the tone and headline the card answered', () => {
    expect(railGlance({ tone: 'amber', headline: '62 %' })).toEqual({
      tone: 'amber',
      headline: '62 %',
    });
    expect(cardToneWord('unread')).toBe('not read');
    expect(cardToneWord('fine')).toBe('fine');
  });

  it('says what a resource link opens, and nothing for no link', () => {
    expect(portalSentence('https://portal.azure.com/#@t/resource/x')).toContain('Azure portal');
    expect(portalSentence('https://app.netlify.com/projects/theyard-edge')).toContain('Netlify');
    expect(portalSentence('https://app.netlify.com/projects/theyard-edge', true)).toContain(
      'new tab'
    );
    // A missing link said "opens in the Azure portal" before (the review of 1.0.4.0, finding 14).
    expect(portalSentence(null)).toBe('');
    expect(portalSentence('https://example.test/elsewhere')).toBe('');
  });
});

describe("a row's detail line", () => {
  it('puts a middle dot between two separate facts and nothing inside one sentence', () => {
    expect(
      detailJoint('dry runs are left out of the averages', 'cost is the API-equivalent price')
    ).toBe(' · ');
    expect(
      detailJoint(
        'run #19 (12:00, bookkeeping): no usage reported',
        'run #18 (12:00, bookkeeping): no usage reported'
      )
    ).toBe(' · ');
    expect(detailJoint('kept in ', 'Cosmos DB')).toBe('');
    expect(detailJoint('no restart · 3 deploys in the window', ' · a blip at 12:01')).toBe('');
    expect(detailJoint('the plan', ', read every minute')).toBe('');
  });
});
