import { describe, expect, it } from 'vitest';
import data from '../../data/recommendations.json';
import {
  RECOMMENDATION_PLACES,
  recommendationsAnswerFrom,
  recommendationsFrom,
} from './recommendations';

const sarah = {
  name: 'Sarah Sebestyen',
  title: 'AI Developer at Hunter Engineering Company',
  profile: 'https://www.linkedin.com/in/sarah-sebestyen/',
  relationship: 'Steven was senior to Sarah but did not manage Sarah directly',
  when: 'November 2024',
  paragraphs: ['First paragraph.', 'Steve consistently delivers high-quality work on time.'],
  pull: 'Steve consistently delivers high-quality work on time.',
};

describe('an answer from /api/recommendations', () => {
  it('is read into the entries, in the order the endpoint lists them', () => {
    const read = recommendationsFrom({
      source: 'https://www.linkedin.com/in/stevenwstout/details/recommendations/',
      recommendations: [sarah, { ...sarah, name: 'Sean Whitesell' }],
    });
    expect(read?.map((entry) => entry.name)).toEqual(['Sarah Sebestyen', 'Sean Whitesell']);
    expect(read?.[0]).toEqual(sarah);
  });

  it('is null when it is not the shape, so a half-shaped answer is a failure and not a card with a hole', () => {
    expect(recommendationsFrom(null)).toBeNull();
    expect(recommendationsFrom({ recommendations: 'ten' })).toBeNull();
    expect(recommendationsFrom({ recommendations: [{ ...sarah, pull: '' }] })).toBeNull();
    expect(recommendationsFrom({ recommendations: [{ ...sarah, profile: '' }] })).toBeNull();
    expect(recommendationsFrom({ recommendations: [{ ...sarah, paragraphs: [] }] })).toBeNull();
    const { name, title, profile, relationship, paragraphs, pull } = sarah;
    expect(
      recommendationsFrom({
        recommendations: [{ name, title, profile, relationship, paragraphs, pull }],
      })
    ).toBeNull();
  });

  it('reads an empty list as an empty list: empty is valid, null is the error', () => {
    expect(recommendationsFrom({ recommendations: [] })).toEqual([]);
  });
});

describe('the derived facts on the answer', () => {
  const fourteen = Array.from({ length: 14 }, (_, index) => ({
    ...sarah,
    name: `Person ${index + 1}`,
  }));
  const groups = [
    {
      key: 'led',
      title: 'People he was senior to',
      lead: 'The view.',
      names: fourteen.map((p) => p.name),
    },
  ];

  it('reads the count, the years and the groups as the API sends them, fourteen entries read as fourteen', () => {
    const read = recommendationsAnswerFrom({
      recommendations: fourteen,
      count: 14,
      from: '2023',
      to: '2026',
      groups,
    });
    expect(read?.count).toBe(14);
    expect(read?.entries).toHaveLength(14);
    expect(read?.groups[0].names).toHaveLength(14);
    expect([read?.from, read?.to]).toEqual(['2023', '2026']);
  });

  it('is null when the facts are missing or out of shape', () => {
    expect(recommendationsAnswerFrom({ recommendations: fourteen })).toBeNull();
    expect(
      recommendationsAnswerFrom({ recommendations: fourteen, count: 14, groups: [{ key: 'led' }] })
    ).toBeNull();
  });

  it('draws one blank place per entry in the file, never a typed number', () => {
    expect(RECOMMENDATION_PLACES).toBe(data.recommendations.length);
  });
});
