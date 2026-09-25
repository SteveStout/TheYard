import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { MACHINE_CHART } from '../../lib/machineChart';
import { BarGauge, MachineChart } from './charts';

/**
 * The Admin tab's charts in the Mark VII grammar (the tweaks pass, B2), held on
 * the drawing itself: no grid, graduations and two bracket ticks, a legend for
 * two series and none for one, a callout on the peak, and a bar gauge whose
 * reading goes inside the fill from 40 per cent.
 */
const slots = (values: (number | null)[]) =>
  values.map((value, index) => ({
    at: `2026-09-25T06:${String(index).padStart(2, '0')}:00Z`,
    value,
  }));

const draw = (
  series: { key: string; name: string; points: ReturnType<typeof slots> }[],
  callout?: { key: string; name: string }
) =>
  renderToStaticMarkup(
    createElement(MachineChart, {
      testId: 'chart',
      label: 'a chart',
      series,
      tones: ['first', 'second'],
      callout,
    })
  );

describe('a chart in the Mark VII grammar', () => {
  it('draws no grid: the only full-width horizontal line is the axis', () => {
    const html = draw([{ key: 'a', name: 'A', points: slots([1, 3, 2, 5, 4]) }]);
    const lines = Array.from(
      html.matchAll(
        /<line[^>]*x1="([\d.]+)"[^>]*y1="([\d.]+)"[^>]*x2="([\d.]+)"[^>]*y2="([\d.]+)"/g
      )
    ).map((match) => match.slice(1).map(Number));
    const full = lines.filter(
      ([x1, y1, x2, y2]) =>
        y1 === y2 && x1 === MACHINE_CHART.left && x2 === MACHINE_CHART.width - MACHINE_CHART.right
    );
    expect(full).toEqual([
      [MACHINE_CHART.left, 146, MACHINE_CHART.width - MACHINE_CHART.right, 146],
    ]);
    // Five up the side, three labelled slots, and six of the nine even steps: the
    // other three sit under a labelled slot and are drawn once, as the major.
    expect(html.match(/data-testid="chart-tick"/g)?.length).toBe(14);
    expect(html.match(/data-testid="chart-bracket"/g)?.length).toBe(2);
  });

  it('carries a legend for two series and none for one', () => {
    const one = draw([{ key: 'a', name: 'A', points: slots([1, 2]) }]);
    const two = draw([
      { key: 'a', name: 'A', points: slots([1, 2]) },
      { key: 'b', name: 'B', points: slots([2, 1]) },
    ]);
    expect(one).not.toContain('data-testid="chart-legend"');
    expect(two).toContain('data-testid="chart-legend"');
  });

  it('writes a single series its unit at the top of the axis, since it has no legend to carry it', () => {
    const html = renderToStaticMarkup(
      createElement(MachineChart, {
        testId: 'chart',
        label: 'a chart',
        unit: 'request units a minute',
        series: [{ key: 'a', name: 'A', points: slots([1, 2]) }],
      })
    );
    expect(html).toMatch(/data-testid="chart-unit"[^>]*>request units a minute</);
  });

  it('calls out the peak of the series it is asked to, and nothing when that series stayed at zero', () => {
    const peaked = draw(
      [
        { key: '5xx', name: 'Server errors', points: slots([0, 0, 0]) },
        { key: '4xx', name: 'Turned away', points: slots([1, 11, 2]) },
      ],
      { key: '4xx', name: 'Turned away' }
    );
    expect(peaked).toContain('data-testid="chart-callout"');
    expect(peaked).toContain('Turned away · peak 11 at');
    // The callout is in the drawing's name too, for a reader who cannot see it.
    expect(peaked).toMatch(/aria-label="a chart; Turned away · peak 11 at/);
    const flat = draw([{ key: '4xx', name: 'Turned away', points: slots([0, 0]) }], {
      key: '4xx',
      name: 'Turned away',
    });
    expect(flat).not.toContain('data-testid="chart-callout"');
  });
});

describe('a bar gauge', () => {
  it('prints the reading after the fill under 40 per cent and inside it from 40', () => {
    const low = renderToStaticMarkup(
      createElement(BarGauge, {
        testId: 'g',
        name: 'Memory',
        ceiling: '1,183 MB',
        value: 120,
        max: 1183,
        reading: '10 % · 120 MB',
      })
    );
    const high = renderToStaticMarkup(
      createElement(BarGauge, {
        testId: 'g',
        name: 'Memory',
        ceiling: '1,183 MB',
        value: 600,
        max: 1183,
        reading: '51 % · 600 MB',
      })
    );
    expect(low).toContain('data-inside="false"');
    expect(high).toContain('data-inside="true"');
    expect(low).toContain('role="meter"');
    expect(high).toContain('--gauge-share:50.7%');
  });

  it('never prints a reading on the gold fill, which no ink clears 4.5 on', () => {
    for (const value of [5, 900]) {
      const html = renderToStaticMarkup(
        createElement(BarGauge, {
          testId: 'g',
          name: 'Request units, busiest minute',
          ceiling: '1,000 / s free',
          value,
          max: 1000,
          reading: `${value} / s`,
          tone: 'gold',
        })
      );
      expect(html).toContain('data-inside="false"');
      const track = html.slice(html.indexOf('role="meter"'));
      expect(track.slice(0, track.indexOf('</div>'))).not.toContain('g-reading');
      expect(html).toContain('data-testid="g-reading"');
    }
  });

  it('is a meter named by its name, read as the reading of the ceiling, within 0 and the ceiling', () => {
    const html = renderToStaticMarkup(
      createElement(BarGauge, {
        testId: 'g',
        name: 'Memory',
        ceiling: '1,183 MB',
        value: 1500,
        max: 1183,
        reading: 'over the plan',
      })
    );
    expect(html).toContain('aria-labelledby="g-name"');
    expect(html).toContain('id="g-name"');
    expect(html).toContain('aria-valuetext="over the plan of 1,183 MB"');
    expect(html).toContain('aria-valuenow="1183"');
  });
});
