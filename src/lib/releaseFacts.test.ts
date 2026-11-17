/**
 * Does:      Holds the release facts the build reads (releaseFacts.ts): a count a day from the changelog's dated
 *            lines for its newest fourteen days, the top version's CodeRabbit row, and the seven Central days ending
 *            today, today included.
 * Does not:  Read the real changelog: the build's own read is held by the landing's browser tests.
 * Used by:   Vitest (npm test).
 */
import { describe, expect, it } from 'vitest';
import { centralDay, releaseFacts, releasesInLastSevenDays } from './releaseFacts';

const line = (version: string, day: string) => `- **${version}** (${day}): words.`;

describe('release facts', () => {
  it('counts the versions a day, newest first, and keeps the top version and its review', () => {
    const changelog = [
      '# Changelog',
      '',
      line('1.0.0.4', '2026-11-17'),
      line('1.0.0.3', '2026-11-17'),
      line('1.0.0.2', '2026-11-15'),
    ].join('\n');
    const facts = releaseFacts(
      changelog,
      { rows: [{ version: '1.0.0.4', reviewed: 'full', findings: { critical: 0 } }] },
      new Date('2026-11-17T18:00:00Z')
    );
    expect(facts).toEqual({
      days: { '2026-11-17': 2, '2026-11-15': 1 },
      version: '1.0.0.4',
      builtOn: '2026-11-17',
      review: { version: '1.0.0.4', reviewed: 'full', critical: 0 },
    });
  });

  it('keeps fourteen days and no more, and has no review until the gate writes one', () => {
    const lines = Array.from({ length: 20 }, (_, i) =>
      line(`1.0.0.${40 - i}`, `2026-10-${String(30 - i).padStart(2, '0')}`)
    );
    const facts = releaseFacts(lines.join('\n'), { rows: [] }, new Date('2026-10-30T18:00:00Z'));
    expect(Object.keys(facts.days)).toHaveLength(14);
    expect(facts.review).toBeNull();
  });

  it('counts the seven days ending today, today included, and nothing older or newer', () => {
    const days = { '2026-11-18': 9, '2026-11-17': 2, '2026-11-11': 3, '2026-11-10': 5 };
    expect(releasesInLastSevenDays(days, '2026-11-17')).toBe(5);
  });

  it('says the Central day, which is the day before in UTC late in the evening', () => {
    expect(centralDay(new Date('2026-11-18T03:30:00Z'))).toBe('2026-11-17');
  });
});
