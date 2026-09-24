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

/**
 * The stores the site runs and how many answered, from the same health answer:
 * one check per store, named "database" and "database (the store)" (ADR: One
 * container, both stores). Null when there is no answer to read.
 */
export function storesUp(
  answer: { status?: unknown; checks?: unknown } | null
): { up: number; of: number } | null {
  if (answer === null || !Array.isArray(answer.checks)) return null;
  const stores = (answer.checks as { name?: unknown; status?: unknown }[]).filter(
    (check) => typeof check.name === 'string' && check.name.startsWith('database')
  );
  if (stores.length === 0) return null;
  return { up: stores.filter((check) => check.status === 'pass').length, of: stores.length };
}
