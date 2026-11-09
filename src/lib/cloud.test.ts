import { describe, expect, it } from 'vitest';
import {
  angleOf,
  arcPath,
  cadenceLine,
  cardToneWord,
  dayMarks,
  detailJoint,
  legendChips,
  linePath,
  pointAt,
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

describe('the cadence line under the dials', () => {
  it('says a minute card is read every minute and unread after three', () => {
    expect(cadenceLine({ cadence_seconds: 60, unread_after_seconds: 180 })).toBe(
      'read every minute, unread after 3 min'
    );
  });
  it('says an hourly card is read every hour and unread after three hours', () => {
    expect(cadenceLine({ cadence_seconds: 3600, unread_after_seconds: 10800 })).toBe(
      'read every hour, unread after 3 h'
    );
  });
  it('names any other cadence in its own unit', () => {
    expect(cadenceLine({ cadence_seconds: 15, unread_after_seconds: 45 })).toBe(
      'read every 15 s, unread after 45 s'
    );
  });
  it('draws nothing for a body from before the cadence was on the wire', () => {
    expect(cadenceLine({})).toBeNull();
    expect(cadenceLine({ cadence_seconds: 60 })).toBeNull();
  });
});

// Each case below kills a mutant the mutation run left alive (ADR-122, the addendum on the local mutation runs):
// a boundary nudged, an operator flipped or a default turned over that no older case could tell apart.
describe('the frame arithmetic, held where the mutation run found it loose', () => {
  it('says a cadence in whole hours, whole minutes, or seconds, and needs both fields', () => {
    expect(cadenceLine({ cadence_seconds: 7200, unread_after_seconds: 3660 })).toBe(
      'read every 2 h, unread after 61 min'
    );
    expect(cadenceLine({ cadence_seconds: 120, unread_after_seconds: 90 })).toBe(
      'read every 2 min, unread after 90 s'
    );
    expect(cadenceLine({ cadence_seconds: 30, unread_after_seconds: 59 })).toBe(
      'read every 30 s, unread after 59 s'
    );
    expect(cadenceLine({ unread_after_seconds: 180 })).toBeNull();
    expect(cadenceLine({ cadence_seconds: 15, unread_after_seconds: 3600 })).toBe(
      'read every 15 s, unread after 1 h'
    );
    expect(cadenceLine({ cadence_seconds: 15, unread_after_seconds: 60 })).toBe(
      'read every 15 s, unread after 1 min'
    );
    expect(cadenceLine({ cadence_seconds: 15, unread_after_seconds: 0 })).toBe(
      'read every 15 s, unread after 0 s'
    );
  });

  it('names the warning tone and keeps the short portal sentence unless the long one is asked for', () => {
    expect(toneWord('warning')).toBe('watch');
    expect(portalSentence('https://portal.azure.com/#@t/resource/x')).toBe(
      'opens in the Azure portal; your sign-in, not the site\u2019s'
    );
    expect(portalSentence('https://portal.azure.com/#@t/resource/x', true)).toBe(
      '(opens the Azure portal in a new tab, with your own Azure sign-in)'
    );
  });

  it('finds the newest reading even when it is the first, and trims and drops empty chips', () => {
    expect(newestValue([5, null])).toBe(5);
    expect(newestValue([5])).toBe(5);
    expect(legendChips(' teal CPU  \u00b7 green memory ')).toEqual(['teal CPU', 'green memory']);
    expect(valueLines('a, , b')).toEqual(['a', 'b']);
    expect(valueLines('')).toEqual([]);
  });

  it('places a point on its circle by the angle, and draws the long way round only past half the ring', () => {
    const right = pointAt(10, 20, 5, 0);
    const down = pointAt(10, 20, 5, 90);
    expect([right.x, right.y]).toEqual([15, 20]);
    expect(down.x).toBeCloseTo(10, 9);
    expect(down.y).toBeCloseTo(25, 9);
    expect(arcPath(100, 100, 50, 0, 50)).toContain(' 0 0 1 ');
    expect(arcPath(100, 100, 50, 0, 51)).toContain(' 0 1 1 ');
    expect(ticks()[1].percent).toBe(2.5);
  });

  it('puts an instant on the window, and a window with no width at its left edge', () => {
    expect(xOf(7, 2, 12, 100)).toBe(50);
    expect(xOf(7, 12, 12, 100)).toBe(0);
    expect(
      linePath([
        { x: 0, y: 1 },
        { x: 1, y: null },
        { x: 2, y: 3 },
        { x: 3, y: 4 },
      ])
    ).toBe('M 0.0 1.0 M 2.0 3.0 L 3.0 4.0');
  });
});
