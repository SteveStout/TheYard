/**
 * The Admin tile's one live reading on the landing page (Steve, 2026-09-22,
 * 1.0.1.4: "a Healthy dot on the Admin tile only"): /api/health, read once
 * when the landing page opens, as a word and a tone. No React here.
 */
export type LandingHealth = 'reading' | 'healthy' | 'degraded' | 'unreachable';

/** What the answer says, or unreachable when there was no answer to read. */
export function landingHealth(answer: { status?: unknown } | null): LandingHealth {
  if (answer === null) return 'unreachable';
  return answer.status === 'healthy' ? 'healthy' : 'degraded';
}

/** The word beside the dot, and the tile's tone: only a healthy thing is green. */
export const HEALTH_WORDS: Record<LandingHealth, { word: string; tone?: 'good' | 'warn' | 'bad' }> =
  {
    reading: { word: 'Checking' },
    healthy: { word: 'Healthy', tone: 'good' },
    degraded: { word: 'Degraded', tone: 'warn' },
    unreachable: { word: 'Unreachable', tone: 'bad' },
  };
