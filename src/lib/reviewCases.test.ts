import { describe, expect, it } from 'vitest';
import file from '../../data/recommendations.json';
import { anchorOf } from './recommendationsPage';
import {
  CASE_VOICES,
  caseDoors,
  caseHeading,
  fromTheWork,
  QUESTIONS_ID,
  REVIEW_CASES,
  WORK,
  type ReviewCase,
} from './reviewCases';
import { PROOF_ADDRESS, RECORD_TILES } from './reviewVerdict';

const byId = (id: ReviewCase['id']) => REVIEW_CASES.find((entry) => entry.id === id)!;

describe("the review route's work, three case cards (ADR: The sales pages set the site's look)", () => {
  it('says the picture’s words, word for word, in the tiles’ order', () => {
    expect(REVIEW_CASES.map((entry) => entry.id)).toEqual([
      'case-conway',
      'case-storee',
      'case-theyard',
    ]);
    expect(WORK.hint).toBe(
      'Each number is on my resume and my LinkedIn. The last one is read from this site as you look at it.'
    );
    expect(byId('case-conway').eyebrow).toBe('Conway Investment Research · Financial services');
    expect(byId('case-conway').heading).toBe('200+ production releases, zero downtime');
    expect(byId('case-storee').heading).toBe('50% shorter release cycle');
    expect(byId('case-theyard').eyebrow).toBe('TheYard · This site, built and run solo');
    expect(byId('case-conway').bullets[2]).toBe('Cut $30,000+ from Azure and third-party spend.');
    expect(byId('case-storee').bullets[0]).toBe(
      'Consolidated four ASP.NET applications onto one .NET 10 platform on Azure with a phased Strangler Fig migration.'
    );
    expect(byId('case-theyard').bullets[2]).toBe(
      'Fifteen code-quality numbers that may only improve, measured on every version.'
    );
    for (const entry of REVIEW_CASES) expect(entry.bullets).toHaveLength(3);
    const conway = byId('case-conway').panel;
    expect(conway.live ? [] : conway.facts.map(([label]) => label)).toEqual([
      'Runtime',
      'Edge',
      'Identity',
      'Delivery',
    ]);
    const storee = byId('case-storee').panel;
    expect(storee.live ? [] : storee.facts.map(([, value]) => value)).toEqual([
      '.NET 10 on Azure',
      'Entity Framework',
      'React and TypeScript',
      'Azure Bicep',
    ]);
    expect(byId('case-theyard').panel).toEqual({ title: 'TheYard, this week', live: true });
  });

  it('heads TheYard with the first screen’s tests figure and never a second count, and never an empty number', () => {
    expect(caseHeading(byId('case-theyard'), '3,679')).toBe(
      '3,679 tests passed on the version you are reading'
    );
    expect(caseHeading(byId('case-theyard'), '')).toBe(
      'Tests passed on the version you are reading'
    );
    expect(caseHeading(byId('case-conway'), '3,679')).toBe(
      '200+ production releases, zero downtime'
    );
    // The two record tiles and the first two cards say the same numbers.
    expect(RECORD_TILES.map((tile) => tile.figure)).toEqual(['200+', '50%']);
    expect(caseHeading(byId('case-conway'), '')).toContain(RECORD_TILES[0].figure);
    expect(caseHeading(byId('case-storee'), '')).toContain(RECORD_TILES[1].figure);
    // Every number on a card is on the locked resume and LinkedIn, or read live: no $70, no Cass, no year added.
    const words = JSON.stringify(REVIEW_CASES);
    expect(words).not.toMatch(/\$70|Cass|\b(19|20)\d\d\b/);
  });

  it('leaves no card a dead end: each card’s last line holds its doors and the actions they report', () => {
    expect(caseDoors(byId('case-conway'))).toEqual([
      {
        label: 'Worked with him at Conway: Meagan Colley, in full',
        href: '/?view=recommendations#meagan-colley',
        action: 'recommendations',
        kind: 'voice',
        arrow: '→',
      },
    ]);
    expect(caseDoors(byId('case-storee'))[0]).toMatchObject({
      label: 'Worked with him at Storee: Henry Crans, in full',
      href: '/?view=recommendations#henry-crans',
      action: 'recommendations',
    });
    expect(
      caseDoors(byId('case-theyard')).map((door) => [door.label, door.href, door.action])
    ).toEqual([
      ['See the proof in 2 minutes', PROOF_ADDRESS, 'proof'],
      ['Or ask it three questions', `#${QUESTIONS_ID}`, 'work'],
    ]);
  });

  it('names two people who exist, whose own paragraphs name the company the card says they worked at', () => {
    for (const voice of CASE_VOICES) {
      const entry = file.recommendations.find((each) => each.name === voice.name);
      expect(entry, voice.name).toBeDefined();
      expect(entry!.paragraphs.join(' ')).toContain(voice.company);
      // A link, never a quote: the door goes to the person's own card on the recommendations page.
      expect(
        REVIEW_CASES.some((card) =>
          card.doors.some((door) => door.href.endsWith(`#${anchorOf(voice.name)}`))
        )
      ).toBe(true);
      expect(JSON.stringify(REVIEW_CASES)).not.toContain(entry!.pull);
    }
  });

  it('links a case and its post both ways once the post is set, and draws no post line while it is not', () => {
    for (const entry of REVIEW_CASES) {
      expect(entry.post).toBeNull();
      expect(caseDoors(entry).some((door) => door.kind === 'post')).toBe(false);
    }
    expect(fromTheWork('any-post', '3,679')).toBeNull();

    const withPost: ReviewCase = { ...byId('case-storee'), post: 'strangler-fig-at-storee' };
    const doors = caseDoors(withPost);
    expect(doors.at(-1)).toEqual({
      label: 'Read the post',
      href: '/?view=blog&post=strangler-fig-at-storee',
      action: 'blog',
      kind: 'post',
      arrow: '→',
      slug: 'strangler-fig-at-storee',
    });
    expect(fromTheWork('strangler-fig-at-storee', '3,679', [withPost])).toEqual({
      line: 'From the work: 50% shorter release cycle',
      href: '/?view=review#case-storee',
    });
    const yard: ReviewCase = { ...byId('case-theyard'), post: 'the-gate' };
    expect(fromTheWork('the-gate', '3,679', [yard])?.line).toBe(
      'From the work: 3,679 tests passed on the version you are reading'
    );
  });
});
