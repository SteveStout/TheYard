import { describe, expect, it } from 'vitest';
import { HEALTH_WORDS, landingHealth } from './landingHealth';

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
