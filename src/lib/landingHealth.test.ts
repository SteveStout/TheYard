import { describe, expect, it } from 'vitest';
import { HEALTH_WORDS, landingHealth, storesUp } from './landingHealth';

describe("the Admin tile's health dot", () => {
  it('reads the one answer: healthy is green, anything else is not', () => {
    expect(landingHealth({ status: 'healthy' })).toBe('healthy');
    expect(landingHealth({ status: 'degraded' })).toBe('degraded');
    expect(landingHealth({})).toBe('degraded');
    expect(landingHealth(null)).toBe('unreachable');
  });

  it('says a word beside the dot, and only healthy is good', () => {
    expect(HEALTH_WORDS.healthy).toEqual({ word: 'Healthy', tone: 'good' });
    expect(HEALTH_WORDS.reading.tone).toBeUndefined();
    expect(HEALTH_WORDS.degraded.tone).toBe('warn');
    expect(HEALTH_WORDS.unreachable.tone).toBe('bad');
  });
});

describe('the stores the landing page reads as up', () => {
  it('counts one check per store from the same health answer', () => {
    const answer = {
      status: 'healthy',
      checks: [
        { name: 'dataset file', status: 'pass' },
        { name: 'database', status: 'pass' },
        { name: 'database (cosmos)', status: 'pass' },
      ],
    };
    expect(storesUp(answer)).toEqual({ up: 2, of: 2 });
    expect(
      storesUp({
        checks: [
          { name: 'database', status: 'pass' },
          { name: 'database (cosmos)', status: 'fail' },
        ],
      })
    ).toEqual({ up: 1, of: 2 });
  });

  it('says nothing rather than zero when there is no answer or no store in it', () => {
    expect(storesUp(null)).toBeNull();
    expect(storesUp({ checks: [{ name: 'docs', status: 'pass' }] })).toBeNull();
    expect(storesUp({ status: 'healthy' })).toBeNull();
  });
});
