import { describe, expect, it } from 'vitest';
import { emptyHourWords } from './telemetryCard';

const at = (iso: string) => (iso === 'bad' ? '' : `time ${iso.slice(11, 16)} UTC`);

describe('an hour Application Insights holds nothing for', () => {
  it('says so and names its newest request instead of drawing zeros', () => {
    const words = emptyHourWords(0, '2026-09-25T18:04:11Z', at);
    expect(words).toContain('holds no request from the last hour');
    expect(words).toContain('time 18:04 UTC');
    expect(words).toContain('daily data cap');
  });

  it('says there is none from the day when the reader found none', () => {
    expect(emptyHourWords(0, null, at)).toContain('none from the last day either');
    expect(emptyHourWords(0, 'bad', at)).toContain('none from the last day either');
  });

  it('says nothing over an hour with requests in it', () => {
    expect(emptyHourWords(12, '2026-09-25T18:04:11Z', at)).toBeNull();
  });
});
