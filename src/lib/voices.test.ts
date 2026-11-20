import { describe, expect, it } from 'vitest';
import file from '../../data/recommendations.json';
import { READ_IN_FULL, VOICES, voiceOf } from './voices';

/** Every source file under src, as text, so the test can say no one's words are typed into one. */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const entries = file.recommendations;

describe('the named voices (ADR: The review route gives a reason before it asks, the addendum on named voices)', () => {
  it('names who speaks where, and each name is one the answer holds', () => {
    expect(VOICES).toEqual({
      band: [
        'Henry Crans',
        'Sarah Sebestyen',
        'Meagan Colley',
        'Sean Whitesell',
        'Khushbu Shah',
        'Anthony Fortner',
      ],
      home: ['Cliff True', 'Sanders Crosby', 'Micah Johnson'],
    });
    for (const name of Object.values(VOICES).flat())
      expect(voiceOf(entries, name)?.name, name).toBe(name);
    expect(READ_IN_FULL).toBe('Read it in full');
  });

  it('finds nothing before the answer or for a name the answer does not hold, so the place closes up', () => {
    expect(voiceOf(null, 'Henry Crans')).toBeNull();
    expect(voiceOf(entries, 'Nobody Here')).toBeNull();
  });

  it('types nobody’s words: no source file holds a line the answer gives', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(100);
    for (const entry of entries)
      for (const [path, text] of Object.entries(sources))
        expect(text.includes(entry.pull), `${entry.name}'s line is typed in ${path}`).toBe(false);
  });
});
