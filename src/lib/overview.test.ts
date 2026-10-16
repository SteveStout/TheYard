import { describe, expect, it } from 'vitest';
import { overviewRows, type OverviewReadings } from './overview';
import type { StatTile } from './statTiles';

const tile = (key: string, value: string, tone: StatTile['tone']): StatTile => ({
  key,
  question: 'up',
  label: key,
  value,
  detail: `${key} detail`,
  tone,
  ringed: false,
});

const base: OverviewReadings = {
  health: {
    status: 'healthy',
    uptime_seconds: 3_600,
    checks: [{ status: 'pass' }, { status: 'pass' }],
    kept_warm: { failed: 0, reads: 46 },
  },
  pages: {
    at: '2026-10-14T16:29:00Z',
    trigger: 'roll',
    version: '1.0.3.124',
    checked: 161,
    up: 161,
  },
  cost: {
    available: true,
    month: '2026-10',
    newest_day: '2026-10-14',
    month_days: 31,
    month_to_date: 13.62,
    forecast_month: 35.49,
    currency: 'USD',
  },
  tiles: [tile('speed', 'under 1 ms', 'good'), tile('errors', '0', 'good')],
  startedAt: '2026-10-14T19:00:00Z',
};

describe('overviewRows', () => {
  it('answers the four questions in order, each with a range under its number', () => {
    const rows = overviewRows(base);
    expect(rows.map((row) => row.key)).toEqual(['up', 'fast', 'cost', 'broke']);
    for (const row of rows) expect(row.range.length).toBeGreaterThan(10);
  });

  it('says every check and page, the last restart and the last roll', () => {
    const up = overviewRows(base)[0];
    expect(up.value).toBe('2 of 2 checks, 161 of 161 pages');
    expect(up.detail).toContain('ago');
    expect(up.detail).toContain('rolled');
    expect(up.detail).toContain('v1.0.3.124');
    expect(up.tone).toBe('good');
  });

  it('puts the day and month in front of a roll that was not today', () => {
    const up = overviewRows({ ...base, pages: { ...base.pages!, at: '2026-01-02T15:00:00Z' } })[0];
    expect(up.detail).toMatch(/rolled \d+ Jan \d\d:\d\d at v1\.0\.3\.124/);
  });

  it('is red on one page down or one failing check', () => {
    const down = overviewRows({ ...base, pages: { ...base.pages!, up: 160 } })[0];
    expect(down.tone).toBe('bad');
    const failing = overviewRows({
      ...base,
      health: { ...base.health!, checks: [{ status: 'pass' }, { status: 'fail' }] },
    })[0];
    expect(failing.tone).toBe('bad');
  });

  it('holds the month to the $50 budget', () => {
    const cost = overviewRows(base)[2];
    expect(cost.value).toBe('$13.62');
    expect(cost.detail).toContain('$35.49');
    expect(cost.range).toContain('$50');
    expect(cost.tone).toBe('warn');
    const over = overviewRows({ ...base, cost: { ...base.cost!, forecast_month: 55 } })[2];
    expect(over.tone).toBe('bad');
    const none = overviewRows({ ...base, cost: { ...base.cost!, available: false } })[2];
    expect(none.tone).toBe('plain');
    expect(none.detail).toBe('this site does not read the bill');
    const slowed = overviewRows({
      ...base,
      cost: {
        ...base.cost!,
        available: false,
        note: 'Cost Management asked this site to slow down',
      },
    })[2];
    expect(slowed.detail).toBe('Cost Management asked this site to slow down');
  });

  it('turns broke red on a failed kept-warm read, and waits for what has not been read', () => {
    const warm = overviewRows({
      ...base,
      health: { ...base.health!, kept_warm: { failed: 2, reads: 46 } },
    })[3];
    expect(warm.tone).toBe('bad');
    expect(warm.detail).toContain('2 kept-warm reads failed');
    const empty = overviewRows({ ...base, health: null, cost: null, tiles: [] });
    expect(empty.map((row) => row.tone)).toEqual(['waiting', 'waiting', 'waiting', 'waiting']);
  });
});
