import { describe, expect, it } from 'vitest';
import { daysLine, funnelSteps, weekLine } from './jobFunnel';

describe('the job hunt funnel card', () => {
  it('gives each step the share of the step before it that went on, and none after a zero', () => {
    const steps = funnelSteps({ counts: [200, 10, 0, 18, 2, 1] });
    expect(steps.map((step) => [step.count, step.ofBefore])).toEqual([
      [200, null],
      [10, 5],
      [0, 0],
      [18, null],
      [2, 11],
      [1, 50],
    ]);
  });

  it('reads a short or negative count as zero, never as a missing step', () => {
    expect(funnelSteps({ counts: [5, -1] }).map((step) => step.count)).toEqual([5, 0, 0, 0, 0, 0]);
  });

  it('says the week against the target, one call and several', () => {
    expect(weekLine({ week_booked: 1, week_target: 2 })).toBe(
      '1 call booked in the last 7 days, against 2 to 3 a week'
    );
    expect(weekLine({ week_booked: 0, week_target: 2 })).toBe(
      '0 calls booked in the last 7 days, against 2 to 3 a week'
    );
  });

  it('says how many days the numbers come from', () => {
    expect([daysLine(0), daysLine(1), daysLine(6)]).toEqual([
      'no funnel line in this window',
      'from 1 day of lines',
      'from 6 days of lines',
    ]);
  });
});
