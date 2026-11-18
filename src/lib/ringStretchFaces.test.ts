import { describe, expect, it } from 'vitest';
import { hourGlance } from './bench';
import { overviewRows, type OverviewReadings } from './overview';
import { ringStretchInFull, tilesFrom, type TileReadings } from './statTiles';

// When the request ring stops short of the hour, every face that reads the hour's speed says
// so in the Traffic card's words, on the line a person sees: the Typical answer tile, the
// overview's "Is it fast?" row that reads the tile's line, and the This hour panel.
const traffic = {
  requests: 500,
  server_errors: 0,
  client_errors: 0,
  warm_requests: 500,
  p50_ms: 0.4,
  p95_ms: 93,
};

const readings = (ringMinutes: number | null): TileReadings => ({
  health: null,
  pages: null,
  traffic: { ...traffic, ring_minutes: ringMinutes },
  memory: null,
  charged: null,
  errors: null,
  visitorsToday: null,
});

const faces = (ringMinutes: number | null) => {
  const tiles = tilesFrom(readings(ringMinutes));
  const speed = tiles.find((tile) => tile.key === 'speed');
  if (speed === undefined) throw new Error('no speed tile');
  const fast = overviewRows({
    health: null,
    pages: null,
    cost: null,
    tiles,
    startedAt: null,
  } as OverviewReadings).find((row) => row.key === 'fast');
  const glance = hourGlance({ ...traffic, ring_minutes: ringMinutes });
  return {
    tile: speed.detail,
    row: fast?.detail ?? '',
    panel: glance.stretch,
    label: glance.label,
  };
};

describe('a request ring cut short of the hour', () => {
  it('is said the Traffic card’s way on the tile, the overview row and the This hour panel', () => {
    const words = `in the ${ringStretchInFull(46)}`;
    expect(words).toBe('in the last 46 minutes, all the request ring holds');
    const seen = faces(46);
    expect(seen.tile).toBe(`95th 93 ms over 500 requests ${words}`);
    expect(seen.row).toBe(seen.tile);
    expect(seen.panel).toBe(words);
    expect(seen.label).toContain(words);
  });

  it('leaves the whole hour as it was, with no stretch on any face', () => {
    const seen = faces(null);
    expect(seen.tile).toBe('95th 93 ms over 500 requests');
    expect(seen.row).toBe(seen.tile);
    expect(seen.panel).toBe('');
    expect(seen.label).toContain("The hour's typical answer");
  });

  it('says it on the line of a tile too quiet to judge', () => {
    const quiet = tilesFrom({
      ...readings(6),
      traffic: { ...traffic, warm_requests: 3, requests: 3, ring_minutes: 6 },
    }).find((tile) => tile.key === 'speed');
    expect(quiet?.detail).toBe('3 requests in the last 6 minutes, all the request ring holds');
  });
});
