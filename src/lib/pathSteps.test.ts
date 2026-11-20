import { describe, expect, it } from 'vitest';
import { currentStep, dockLine, PATH_LABEL, PATH_STEPS, stepState } from './pathSteps';

describe('the numbered path (sales1120)', () => {
  it('names the six steps in the page’s order, each with its section’s anchor', () => {
    expect(PATH_STEPS.map((step) => `${step.number} ${step.name} #${step.id}`)).toEqual([
      '01 The work #the-work',
      '02 Three questions #questions',
      '03 Who worked with him #voices',
      '04 From the blog #blog',
      '05 The tour #review-tour',
      '06 Talk #talk',
    ]);
    expect(PATH_LABEL).toBe('The path down this page');
  });

  it('marks current the last step whose section’s top has passed 45 per cent of the window', () => {
    // A window 900 tall: 405 is the line.
    expect(currentStep([1200, 2000, 2600, 3200, 3800, 4400], 900)).toBe(-1);
    expect(currentStep([404, 1100, 1700, 2300, 2900, 3500], 900)).toBe(0);
    expect(currentStep([-900, 405, 1000, 1600, 2200, 2800], 900)).toBe(0);
    expect(currentStep([-1400, -300, 300, 900, 1500, 2100], 900)).toBe(2);
    // A section the page has not drawn (the blog with no post) is passed over, never current.
    expect(currentStep([-2000, -1200, -500, null, 200, 900], 900)).toBe(4);
    expect(currentStep([-5000, -4000, -3000, -2000, -1000, 100], 900)).toBe(5);
  });

  it('at the page’s foot, where the close can never rise that high, marks the last section in view current', () => {
    const foot = [-4000, -3000, -2000, -1200, -300, 520];
    expect(currentStep(foot, 900)).toBe(4);
    expect(currentStep(foot, 900, true)).toBe(5);
    expect(currentStep([-4000, -3000, -2000, -1200, -300, 950], 900, true)).toBe(4);
  });

  it('draws the steps before the current one done, the current one now, and says where the phone reader is', () => {
    expect([0, 1, 2, 3, 4, 5].map((index) => stepState(index, 2))).toEqual([
      'done',
      'done',
      'now',
      '',
      '',
      '',
    ]);
    expect(stepState(0, -1)).toBe('');
    expect(dockLine(1)).toBe('02 of 06, Three questions');
    expect(dockLine(5)).toBe('06 of 06, Talk');
    expect(dockLine(-1)).toBe('');
  });
});
