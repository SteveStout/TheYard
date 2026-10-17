import { describe, expect, it } from 'vitest';
import { recommendationsFrom } from './recommendations';

const sarah = {
  name: 'Sarah Sebestyen',
  title: 'AI Developer at Hunter Engineering Company',
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
    expect(recommendationsFrom({ recommendations: [{ ...sarah, paragraphs: [] }] })).toBeNull();
    const { name, title, relationship, paragraphs, pull } = sarah;
    expect(
      recommendationsFrom({ recommendations: [{ name, title, relationship, paragraphs, pull }] })
    ).toBeNull();
  });

  it('reads an empty list as an empty list: empty is valid, null is the error', () => {
    expect(recommendationsFrom({ recommendations: [] })).toEqual([]);
  });
});
