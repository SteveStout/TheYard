import { describe, expect, it } from 'vitest';
import {
  BENCH_CARDS,
  BENCH_QUESTIONS,
  cardForTile,
  findCards,
  HOUR_RING_MS,
  hourGlance,
  msWords,
  neighbours,
  TILE_CARD,
} from './bench';
import { CARD_SLUGS, cardFromAddress, pinFromAddress } from './workbench';

describe('the workbench (ADR: The Admin tab, as a product, the addendum on the workbench)', () => {
  it('has every card once, under one of the five questions, in the order the slugs are listed', () => {
    expect(BENCH_CARDS.map((card) => card.slug)).toEqual([...CARD_SLUGS]);
    expect(new Set(CARD_SLUGS).size).toBe(19);
    const questions = BENCH_QUESTIONS.map((question) => question.key);
    expect(questions).toEqual(['up', 'fast', 'cost', 'broke', 'desk']);
    // The rail's groups never interleave: a question's cards sit together, in the questions' order.
    const order = BENCH_CARDS.map((card) => questions.indexOf(card.question));
    expect(order).toEqual([...order].sort((a, b) => a - b));
  });

  it('finds each card by the test id every spec already uses', () => {
    for (const card of BENCH_CARDS) {
      expect(card.testId).toMatch(/-card$/);
    }
    expect(BENCH_CARDS.find((card) => card.slug === 'kept')?.testId).toBe('kept-logs-card');
    expect(BENCH_CARDS.find((card) => card.slug === 'reset')?.testId).toBe('reset-link-card');
  });

  it('opens the card an address names, and health for no name or a wrong one', () => {
    expect(cardFromAddress('timing')).toEqual({ slug: 'timing', asked: 'timing', known: true });
    expect(cardFromAddress(null)).toEqual({ slug: 'health', asked: null, known: true });
    expect(cardFromAddress('  ')).toEqual({ slug: 'health', asked: null, known: true });
    expect(cardFromAddress('nope')).toEqual({ slug: 'health', asked: 'nope', known: false });
    // A name is a card's slug exactly: a test id is not an address.
    expect(cardFromAddress('timing-card').known).toBe(false);
  });

  it('pins only a real card, and may pin the open one', () => {
    expect(pinFromAddress('errors')).toBe('errors');
    expect(pinFromAddress('health')).toBe('health');
    expect(pinFromAddress('nope')).toBeNull();
    expect(pinFromAddress(null)).toBeNull();
  });

  it('walks the rail with j and k, wrapping at both ends', () => {
    expect(neighbours('timing')).toEqual({ previous: 'telemetry', next: 'backends' });
    expect(neighbours('health').previous).toBe('reset');
    expect(neighbours('reset').next).toBe('health');
    for (const slug of CARD_SLUGS) {
      expect(neighbours(neighbours(slug).next).previous).toBe(slug);
    }
  });

  it('finds cards by every word typed, in the name, the slug or the question', () => {
    expect(findCards('').length).toBe(19);
    expect(findCards('timing').map((card) => card.slug)).toEqual(['timing']);
    expect(findCards('SQL').map((card) => card.slug)).toContain('sql');
    expect(findCards('what broke').map((card) => card.slug)).toEqual(['errors', 'log', 'kept']);
    expect(findCards('nothing like this')).toEqual([]);
  });

  it('sends every tile to the card that answers it', () => {
    expect(Object.keys(TILE_CARD).sort()).toEqual(
      ['charged', 'errors', 'health', 'memory', 'pages', 'speed', 'version', 'visitors'].sort()
    );
    expect(cardForTile('speed')).toBe('timing');
    expect(cardForTile('visitors')).toBe('activity');
    expect(cardForTile('something new')).toBe('health');
  });
});

describe('the hour at a glance, beside the open card', () => {
  it('reads the hour the strip read, as two rings against ten milliseconds and a readout', () => {
    const glance = hourGlance({
      requests: 1234,
      server_errors: 0,
      client_errors: 3,
      p50_ms: 0.6,
      p95_ms: 5.9,
    });
    expect(glance.ring).toEqual({ p95: 5.9, p50: 0.6, max: HOUR_RING_MS });
    expect(glance.inside).toBe('< 1 ms');
    expect(glance.rows).toEqual([
      ['Typical answer', 'under 1 ms'],
      ['Ninety-fifth', '6 ms'],
      ['Requests', '1,234'],
      ['Server errors', '0'],
      ['Turned away', '3'],
    ]);
    expect(glance.label).toContain('against 10 ms');
  });

  it('says what it has not read rather than drawing a zero', () => {
    const glance = hourGlance(null);
    expect(glance.ring).toBeNull();
    expect(glance.rows.every(([, value]) => value === 'not read yet')).toBe(true);
    expect(msWords(null)).toBe('none');
    expect(msWords(12.4)).toBe('12 ms');
  });
});
