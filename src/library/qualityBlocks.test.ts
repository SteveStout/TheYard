import { describe, expect, it } from 'vitest';
import { parseTrend } from '../lib/qualityTrend';
import { qualityBlock } from './qualityBlocks';

describe('the Quality page fence', () => {
  it('draws a figure with the caption, a sentence from first to last, and the readings for the chart', () => {
    const fence =
      'Declarations with no summary | may only fall | 1.0.4.115\n2026-10-15 |  | 239 | pre-scan\n2026-11-10 | 1.0.4.178 | 399 | quality.py';
    const html = qualityBlock('quality-trend', fence);
    expect(html).toContain(
      '<strong>Declarations with no summary</strong> <span>may only fall</span>'
    );
    expect(html).toContain('239 on 2026-10-15 (the pre-scan), 399 at 1.0.4.178, over 2 readings.');
    const data = /data-quality-trend="([^"]*)"/.exec(html)?.[1] ?? '';
    const parsed = JSON.parse(data.replace(/&quot;/g, '"').replace(/&amp;/g, '&')) as ReturnType<
      typeof parseTrend
    >;
    expect(parsed).toEqual(parseTrend(fence));
  });

  it('says so in words when the build has no readings, and escapes what it is given', () => {
    const html = qualityBlock('quality-trend', 'A <b>name</b> | may only fall | 1.0.4.115');
    expect(html).toContain('No readings in this build.');
    expect(html).toContain('A &lt;b&gt;name&lt;/b&gt;');
  });
});
