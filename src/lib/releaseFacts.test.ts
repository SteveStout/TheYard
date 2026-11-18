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
    const history = {
      first_ratchet_version: '1.0.0.2',
      rows: [
        { version: '1.0.0.1', date: '2026-11-14', source: 'quality.py', numbers: { a: 3 } },
        { version: '1.0.0.2', date: '2026-11-15', source: 'pre-scan', numbers: { a: 2 } },
        { version: '1.0.0.3', date: '2026-11-17', source: 'quality.py', numbers: { a: 2 } },
        { version: '1.0.0.4', date: '2026-11-17', source: 'quality.py', numbers: { a: 1 } },
      ],
    };
    const facts = releaseFacts(
      changelog,
      { rows: [{ version: '1.0.0.4', reviewed: 'full', findings: { critical: 0 } }] },
      new Date('2026-11-17T18:00:00Z'),
      history,
      ['a']
    );
    expect(facts).toEqual({
      days: { '2026-11-17': 2, '2026-11-15': 1 },
      version: '1.0.0.4',
      builtOn: '2026-11-17',
      review: { version: '1.0.0.4', reviewed: 'full', critical: 0 },
      // The badge needs the newest measured reading and the one before it, never the whole history.
      latest: [history.rows[2], history.rows[3]],
      // The landing needs two counts, the run held, and one number's line: worked out here, at the build.
      quality: {
        measures: 1,
        versions: 3,
        held: { version: '1.0.0.1', date: '2026-11-14', releases: 3 },
        marker: '1.0.0.2',
        trends: {
          a: [
            { date: '2026-11-14', version: '1.0.0.1', value: 3, preScan: false },
            { date: '2026-11-15', version: '1.0.0.2', value: 2, preScan: true },
            { date: '2026-11-17', version: '1.0.0.3', value: 2, preScan: false },
            { date: '2026-11-17', version: '1.0.0.4', value: 1, preScan: false },
          ],
        },
      },
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
