import { describe as suite, expect, it } from 'vitest';
import { countsLine, describe, groupEventsByDay, queryFor, toneOf, type LogEvent } from './logs';

/**
 * The kept log card's arithmetic (ADR: Logs that outlive the container).
 * What is worth holding: the query string carries only what narrows, a
 * status has to be three digits to travel, the path is encoded so it is
 * never syntax, a request describes itself as a request line and an error
 * as its first line of detail, and the day grouping is newest first.
 */

const event = (over: Partial<LogEvent>): LogEvent => ({
  at: '2026-09-13T12:00:00Z',
  kind: 'request',
  store: 'sql',
  level: 'Information',
  category: 'request',
  method: 'GET',
  path: '/api/vehicles',
  status: 200,
  duration_ms: 3,
  visitor: '0123456789abcdef0123456789abcdef',
  network: '203.0.113.x',
  message: '',
  detail: '',
  trace_id: 't',
  ...over,
});

suite('queryFor', () => {
  it('carries only the parts that narrow anything', () => {
    expect(queryFor('7d', { kind: '', status: '', path: '' })).toBe('window=7d');
    expect(queryFor('24h', { kind: 'error', status: ' 500 ', path: '' })).toBe(
      'window=24h&kind=error&status=500'
    );
  });

  it('drops a status that is not three digits and encodes the path', () => {
    expect(queryFor('30d', { kind: '', status: '5xx', path: 'a b&c=d' })).toBe(
      'window=30d&path=a%20b%26c%3Dd'
    );
    expect(queryFor('30d', { kind: '', status: '', path: 'x'.repeat(200) })).toBe(
      `window=30d&path=${'x'.repeat(80)}`
    );
  });
});

suite('describe', () => {
  it('reads a request as its request line', () => {
    expect(describe(event({ status: 404, duration_ms: 12 }))).toBe(
      'GET /api/vehicles 404 in 12 ms'
    );
  });

  it('reads an error as its message and the first line of its detail', () => {
    const e = event({
      kind: 'error',
      message: 'it broke',
      detail: 'InvalidOperationException: boom\n   at somewhere',
    });
    expect(describe(e)).toBe('it broke (InvalidOperationException: boom)');
    expect(describe(event({ kind: 'app', category: 'TheYard.Room', message: 'slow' }))).toBe(
      'slow'
    );
  });
});

suite('the rest', () => {
  it('summarises the counts in one line with zeros where a kind is missing', () => {
    expect(countsLine([{ kind: 'request', count: 12 }])).toBe('12 requests, 0 errors, 0 warnings');
  });

  it('groups by UTC day, newest day first, keeping the order within a day', () => {
    const groups = groupEventsByDay([
      event({ at: '2026-09-13T12:00:00Z', path: '/a' }),
      event({ at: '2026-09-12T23:59:59Z', path: '/b' }),
      event({ at: '2026-09-13T11:00:00Z', path: '/c' }),
    ]);
    expect(groups.map((g) => g.day)).toEqual(['2026-09-13', '2026-09-12']);
    expect(groups[0]?.events.map((e) => e.path)).toEqual(['/a', '/c']);
  });

  it('tones a line by its kind and its status', () => {
    expect(toneOf(event({}))).toBe('plain');
    expect(toneOf(event({ status: 404 }))).toBe('warn');
    expect(toneOf(event({ status: 503 }))).toBe('error');
    expect(toneOf(event({ kind: 'app' }))).toBe('warn');
    expect(toneOf(event({ kind: 'error', status: 0 }))).toBe('error');
  });
});
