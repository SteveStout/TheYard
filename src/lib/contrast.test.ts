import { describe, expect, it } from 'vitest';
import { contrast, hexTokens } from './contrast';

describe('hexTokens', () => {
  it('reads a hex token as written, in the order written', () => {
    expect(hexTokens(':root { --a: #AABBCC; --b: #000000; }')).toEqual([
      { name: '--a', hex: '#aabbcc' },
      { name: '--b', hex: '#000000' },
    ]);
  });

  it('reads an alias through to the hex it comes to, however many steps away', () => {
    const sheet = ':root { --c: var(--b); --b: var(--a); --a: #123456; }';
    expect(hexTokens(sheet)).toEqual([
      { name: '--c', hex: '#123456' },
      { name: '--b', hex: '#123456' },
      { name: '--a', hex: '#123456' },
    ]);
  });

  it('takes the first value a name is given, as the sheet does before its fallbacks', () => {
    expect(hexTokens(':root { --a: #111111; } @media x { :root { --a: #222222; } }')).toEqual([
      { name: '--a', hex: '#111111' },
    ]);
  });

  it("takes a name whose first value is no hex as no colour, not as a fallback's hex", () => {
    const sheet =
      ':root { --glass: rgba(255, 255, 255, 0.3); } @supports not (x) { :root { --glass: #ffffff; } }';
    expect(hexTokens(sheet)).toEqual([]);
  });

  it('leaves out a mix, an alias that reaches no hex, and a loop', () => {
    const sheet =
      ':root { --m: color-mix(in srgb, var(--a) 5%, transparent); --x: var(--nowhere); --p: var(--q); --q: var(--p); --a: #010203; }';
    expect(hexTokens(sheet)).toEqual([{ name: '--a', hex: '#010203' }]);
  });
});

describe('contrast', () => {
  it('measures the way WCAG does: white on black is 21:1, and a colour on itself 1:1', () => {
    expect(contrast('#ffffff', '#000000')).toBeCloseTo(21, 5);
    expect(contrast('#524b48', '#524b48')).toBe(1);
  });
});
