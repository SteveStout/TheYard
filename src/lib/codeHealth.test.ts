import { describe, expect, it } from 'vitest';
import { codeRows, gateTrend } from './codeHealth';

describe('code health', () => {
  it('reads every number the gate measured, and red on one warning', () => {
    const rows = codeRows(
      { warnings: 1, distJsBytes: 350_000, distCssBytes: 40_960, outdatedNpm: 3, outdatedNuget: 0 },
      240
    );
    expect(rows.map((row) => row.key)).toEqual(['warnings', 'gate', 'bundle', 'outdated']);
    expect(rows[0]).toMatchObject({ value: '1 warning', tone: 'bad' });
    expect(rows[1]).toMatchObject({ value: '240 s', tone: 'good' });
    expect(rows[2].value).toBe('341.8 kB of script, 40 kB of styles');
    expect(rows[3].value).toBe('3 npm, 0 NuGet');
  });

  it('says what the gate did not measure rather than a zero', () => {
    const rows = codeRows(undefined, 320);
    expect(rows[0]).toMatchObject({ value: 'not measured by this gate', tone: 'plain' });
    expect(rows[1].tone).toBe('warn');
    expect(rows[3].value).toBe('not measured by this gate');
  });

  it('draws the kept gates oldest first against the five-minute line', () => {
    const trend = gateTrend([
      {
        version: 'a',
        ranAt: '2026-10-14T10:00:00Z',
        gateSeconds: 280,
        passed: 1,
        failed: 0,
        distJsBytes: null,
      },
      {
        version: 'b',
        ranAt: '2026-10-15T10:00:00Z',
        gateSeconds: 300,
        passed: 1,
        failed: 0,
        distJsBytes: null,
      },
    ]);
    expect(trend.points.map((point) => point.value)).toEqual([280, 300]);
    expect(trend.amber).toBe(300);
  });
});
