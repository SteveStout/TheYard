import { describe, expect, it } from 'vitest';
import type { Recommendation, RecommendationsAnswer } from './recommendations';
import {
  anchorOf,
  cardAddress,
  indexOf,
  sectionsOf,
  tilesOf,
  titleOf,
} from './recommendationsPage';

const entry = (name: string): Recommendation => ({
  name,
  title: 'Engineer',
  profile: `https://www.linkedin.com/in/${anchorOf(name)}/`,
  relationship: 'Steven was senior to them but did not manage them directly',
  when: 'May 2024',
  paragraphs: ['One sentence.'],
  pull: 'One sentence.',
});

const answer: RecommendationsAnswer = {
  entries: [entry('Cliff True'), entry('Henry Crans'), entry('Renée O’Brien')],
  count: 3,
  from: '2023',
  to: '2026',
  groups: [
    {
      key: 'led',
      title: 'People he was senior to',
      lead: 'The view.',
      names: ['Henry Crans', 'Renée O’Brien'],
    },
    {
      key: 'outside',
      title: 'People from outside the company',
      lead: 'Partners.',
      names: ['Cliff True', 'Nobody'],
    },
  ],
};

describe('the recommendations page', () => {
  it('anchors a card by the name alone, accents and marks dropped', () => {
    expect(anchorOf('Henry Crans')).toBe('henry-crans');
    expect(anchorOf('Renée O’Brien')).toBe('renee-o-brien');
    expect(cardAddress('Cliff True')).toBe('/?view=recommendations#cliff-true');
  });

  it('draws the groups the API answered in its order, each entry once, a name with no entry left out', () => {
    const sections = sectionsOf(answer);
    expect(sections.map((s) => s.key)).toEqual(['led', 'outside']);
    expect(sections[1].entries.map((e) => e.name)).toEqual(['Cliff True']);
    expect(indexOf(sections).map((i) => i.anchor)).toEqual([
      'henry-crans',
      'renee-o-brien',
      'cliff-true',
    ]);
  });

  it('reads its title and its tiles from the answer, never a typed number', () => {
    expect(titleOf(answer)).toBe('3 people who worked with Steven');
    expect(tilesOf(answer).map((t) => t.figure)).toEqual(['3', '2023 to 2026', '2']);
    expect(tilesOf({ ...answer, from: '2026', to: '2026' })[1].figure).toBe('2026');
  });
});
