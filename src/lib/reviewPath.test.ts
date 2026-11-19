import { describe, expect, it } from 'vitest';
import type { StorageLike } from './reviewProgress';
import {
  bidWords,
  markPath,
  PATH,
  PATH_KEY,
  PATH_STOPS,
  pathProgress,
  pathTrend,
  readPath,
  roomAddress,
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
    expect(PATH.open).toBe('Open this stop');
    expect(pathProgress([])).toBe('0 of 3 stops');
    expect(pathProgress(['gate'])).toBe('1 of 3 stops');
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

  it('keeps the place in the path’s own order whichever stop is opened first, and opening one twice changes nothing', () => {
    const storage = memory();
    let reached = markPath(storage, [], 'room');
    reached = markPath(storage, reached, 'gate');
    reached = markPath(storage, reached, 'room');
    expect(reached).toEqual(['gate', 'room']);
    expect(pathProgress(reached)).toBe('2 of 3 stops');
    expect(stopAddress('room', lot)).toBe('/?vehicle=v%2042');
  });
});
