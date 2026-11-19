import { describe, expect, it } from 'vitest';
import type { StorageLike } from './reviewProgress';
import {
  bidWords,
  closesIn,
  lotAmount,
  markPath,
  PATH,
  PATH_KEY,
  PATH_STOPS,
  pathProgress,
  pathTrend,
  readPath,
  roomAddress,
  SPARK,
  sparkGeometry,
  stopAddress,
  storeTotal,
  type PathLot,
} from './reviewPath';

/** A Storage held in a map, or one that refuses every call, the way a private window can. */
function memory(
  seed: Record<string, string> = {},
  refuse = false
): StorageLike & { held: Map<string, string> } {
  const held = new Map(Object.entries(seed));
  return {
    held,
    getItem: (key) => {
      if (refuse) throw new Error('refused');
      return held.get(key) ?? null;
    },
    setItem: (key, value) => {
      if (refuse) throw new Error('refused');
      held.set(key, value);
    },
  };
}

const lot: PathLot = {
  live: true,
  vehicle_id: 'v 42',
  name: '2019 Honda Civic EX',
  current_bid: 12500,
  ends_at_ms: 1,
  total: 100000,
};

describe('the review route short path (ADR: The review route gives a reason before it asks, the addendum on the short path)', () => {
  it('says the design’s three stops, word for word, each with its time and the action its button reports', () => {
    expect(PATH_STOPS.map((stop) => [stop.title, stop.time, stop.action])).toEqual([
      ['Quality held with AI', '1 min', 'stop-quality'],
      ['The gate every push passes', '30 sec', 'stop-gate'],
      ['A system that is really running', '30 sec', 'stop-room'],
    ]);
    expect(PATH.title).toBe('Two minutes, three stops');
    expect(PATH.hint).toBe(
      'Each tile is the running system, read live. Open one and you are inside it.'
    );
    expect(PATH.open).toBe('Open this stop');
    expect(pathProgress([])).toBe('0 of 3 opened.');
    expect(pathProgress(['gate'])).toBe('1 of 3 opened.');
  });

  it('keeps its place under its own key, apart from the tour’s, in the path’s order, and goes on when the store refuses', () => {
    const storage = memory();
    const once = markPath(storage, [], 'room');
    const twice = markPath(storage, once, 'quality');
    expect(twice).toEqual(['quality', 'room']);
    expect(readPath(storage)).toEqual(['quality', 'room']);
    expect([...storage.held.keys()]).toEqual([PATH_KEY]);
    expect(readPath(memory({ [PATH_KEY]: '{"not":"a list"}' }))).toEqual([]);
    expect(readPath(memory({ [PATH_KEY]: 'not json' }))).toEqual([]);
    expect(markPath(memory({}, true), [], 'gate')).toEqual(['gate']);
    expect(readPath(null)).toEqual([]);
  });

  it('lands each stop on its payoff: Quality at the slip and the turn, the tests card, the lot or the inventory', () => {
    expect(stopAddress('quality', null)).toBe('/?doc=quality#the-slip-and-the-turn');
    expect(stopAddress('gate', null)).toBe('/?view=admin&card=tests');
    expect(roomAddress(lot)).toBe('/?vehicle=v%2042');
    expect(roomAddress({ ...lot, live: false, vehicle_id: null })).toBe('/?view=inventory');
    expect(roomAddress(null)).toBe('/?view=inventory');
  });

  it('prints the lot and the chart only from their answers', () => {
    expect(storeTotal(lot)).toBe('100,000');
    expect(storeTotal(null)).toBe('');
    expect(bidWords(lot)).toBe('$12,500');
    expect(bidWords({ ...lot, current_bid: null })).toBe('');
    expect(pathTrend(null, '1.0.4.115')).toBeNull();
    expect(pathTrend([], '1.0.4.115')).toBeNull();
    const data = pathTrend(
      ['2026-10-15 | 1.0.4.100 | 12 | quality.py', '2026-10-20 | 1.0.4.115 | 30 | quality.py'],
      '1.0.4.115'
    );
    expect(data?.marker).toBe('1.0.4.115');
    expect(data?.points.map((point) => point.value)).toEqual([12, 30]);
  });

  it('prints the lot’s bid with the room bidding, and nothing before the answer', () => {
    expect(lotAmount(lot)).toEqual({ label: 'Current bid', amount: '$12,500', bidding: true });
    expect(lotAmount(null)).toEqual({ label: 'Current bid', amount: '', bidding: false });
  });

  it('counts the lot down from its close on the page’s clock, never below none', () => {
    expect(closesIn(252_000, 0)).toBe('4:12');
    expect(closesIn(252_999, 0)).toBe('4:12');
    expect(closesIn(3_605_000, 0)).toBe('60:05');
    expect(closesIn(1_000, 5_000)).toBe('0:00');
  });

  it('draws the chart as the picture: one line scaled to the tile, the area up to the ratchet, the mark at its version', () => {
    expect(sparkGeometry(null, '1.0.4.115')).toBeNull();
    expect(sparkGeometry(['2026-10-15 | 1.0.4.100 | 12 | quality.py'], '1.0.4.115')).toBeNull();
    // Day 0, day 4, day 12 (the ratchet) and day 35: each reading at its day across the 348 units between the insets.
    const readings = [
      '2026-10-15 | 1.0.3.139 | 50 | quality.py',
      '2026-10-19 | 1.0.3.186 | 110 | quality.py',
      '2026-10-27 | 1.0.4.115 | 100 | quality.py',
      '2026-11-19 | 1.0.5.112 | 10 | quality.py',
    ];
    const spark = sparkGeometry(readings, '1.0.4.115');
    // Higher is worse: the history's highest reading is at the top, its lowest at the bottom.
    expect(spark?.before).toBe('M6 49.2 L45.8 6 L125.3 13.2');
    expect(spark?.after).toBe('M125.3 13.2 L354 78');
    expect(spark?.area).toBe(
      `M6 49.2 L45.8 6 L125.3 13.2 L125.3 ${SPARK.floor} L6 ${SPARK.floor} Z`
    );
    expect(spark?.markX).toBe(125.3);
    // Two versions on one day stand one above the other; a history all on one day is spread by order instead.
    expect(
      sparkGeometry(
        ['2026-10-15 | a | 1 | q', '2026-10-15 | b | 2 | q', '2026-10-16 | c | 3 | q'],
        'b'
      )?.markX
    ).toBe(6);
    expect(sparkGeometry(['2026-10-15 | a | 1 | q', '2026-10-15 | b | 2 | q'], 'z')?.markX).toBe(
      354
    );
    // A history without the ratchet's version marks its last reading.
    expect(sparkGeometry(readings.slice(0, 2), '1.0.4.115')?.markX).toBe(354);
  });

  it('keeps the place in the path’s own order whichever stop is opened first, and opening one twice changes nothing', () => {
    const storage = memory();
    let reached = markPath(storage, [], 'room');
    reached = markPath(storage, reached, 'gate');
    reached = markPath(storage, reached, 'room');
    expect(reached).toEqual(['gate', 'room']);
    expect(pathProgress(reached)).toBe('2 of 3 opened.');
    expect(stopAddress('room', lot)).toBe('/?vehicle=v%2042');
  });
});
