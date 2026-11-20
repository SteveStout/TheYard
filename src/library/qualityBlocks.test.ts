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

describe('the Quality page blocks (the quality1120 picture)', () => {
  it('draws a small multiple with its value now, its rule and first reading, and why it matters', () => {
    const fence =
      'Declarations with no summary | may only fall | 1.0.4.115 | small | A public type is read.\n2026-10-15 | 1.0.3.139 | 240 | quality.py\n2026-11-20 | 1.0.5.122 | 1389 | quality.py';
    const html = qualityBlock('quality-trend', fence);
    expect(html).toContain('data-form="small"');
    expect(html).toContain('<strong class="quality-now">1,389</strong>');
    expect(html).toContain('may only fall · 240 on the first reading');
    expect(html).toContain('<p class="quality-why">A public type is read.</p>');
    const floor = qualityBlock(
      'quality-trend',
      'Line coverage, per cent | floor 90 | | small\n2026-11-20 | 1.0.5.122 | 94.2 | ci'
    );
    expect(floor).toContain('<strong class="quality-now">94.2%</strong>');
    expect(floor).toContain('floor 90, no change may lower it');
  });

  it('draws the readouts, each its label, value, what it is out of and its line', () => {
    const html = qualityBlock(
      'quality-readouts',
      'Numbers held | 15 | of 15 | at or under\nRules | 231 | | checked'
    );
    expect(html.match(/class="quality-readout"/g)).toHaveLength(2);
    expect(html).toContain('<span class="quality-readout-value">15 <small>of 15</small></span>');
    expect(html).toContain('<span class="quality-readout-value">231</span>');
    expect(html).not.toContain('op-live-panel');
  });

  it('draws a readouts fence with a head as the dark live panel, its head and LIVE tag over live values', () => {
    const html = qualityBlock(
      'quality-readouts',
      'Numbers held | 15 | of 15 | at or under\nRules | 231 | | checked',
      'The ratchet, right now'
    );
    expect(html).toContain('class="quality-readouts op-live-panel"');
    expect(html).toContain('data-mock="live-panel"');
    expect(html).toContain('<span>The ratchet, right now</span>');
    expect(html).toContain('<i class="op-live-dot" aria-hidden="true"></i>Live');
    expect(html.match(/data-mock="live-value"/g)).toHaveLength(2);
    expect(html).toContain(
      '<span class="quality-readout-value" data-mock="live-value">15 <small>of 15</small></span>'
    );
  });

  it('draws the held pill and a tile per number, its bar by kind', () => {
    const html = qualityBlock(
      'quality-tiles',
      [
        '15 of 15 at or under their limit on 1.0.5.122',
        'Async | 1 | fall | 2 | 107 at the ratchet, may only fall',
        'xUnit | 4,555 | rise | 50 | 2,255 at the ratchet, may only rise',
        'Blocking waits | 0 | zero | 0 | peaked at 2 before the ratchet; zero since',
      ].join('\n')
    );
    expect(html).toContain('data-all="true"');
    expect(html.match(/class="quality-tile op-glass op-tile"/g)).toHaveLength(3);
    expect(html).toContain('<rect class="quality-bar-now" x="0" y="0" width="2"');
    expect(html).toContain('<rect class="quality-bar-was" x="0" y="0" width="50"');
    expect(html).toContain('<rect class="quality-bar-gain" x="50" y="0" width="50"');
    expect(html).toContain('<line class="quality-bar-held"');
    expect(qualityBlock('quality-tiles', '14 of 15 at or under')).toContain('data-all="false"');
  });

  it('draws a rulebook card with its gauge and says "of" only when a run failed', () => {
    const html = qualityBlock(
      'quality-groups',
      'Shape | P. | E. | 48 | 53 | 538 | 538\nWorks | P. | E. | 1 | 2 | 9 | 10'
    );
    expect(html.match(/<article class="quality-group op-glass op-tile">/g)).toHaveLength(2);
    expect(html).toContain('<strong>48 of 53</strong> rules checked by the gate');
    expect(html).toContain('<strong>538</strong> runs passed');
    expect(html).toContain('<strong>9 of 10</strong> runs passed');
    expect(html).toContain('width="50"');
  });
});
