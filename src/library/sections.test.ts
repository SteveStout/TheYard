import { describe, expect, it } from 'vitest';
import { RECORDS } from './records';
import { LINKS, MENUS } from './sections';
import { RESUME_ADDRESS } from '../lib/reviewVerdict';

describe('the Decision Records menu', () => {
  // The landing page counts this menu's rows as the number of decision
  // records, so a record left out of it is missing from the sidebar and
  // missing from the count.
  it('lists every decision record once, and nothing that is not one', () => {
    const listed = MENUS.records.items.map((item) => item.key);
    expect(new Set(listed).size).toBe(listed.length);
    expect([...listed].sort()).toEqual(Object.keys(RECORDS).sort());
  });

  it('lists them in number order, whichever run of records each one is written in', () => {
    const numbers = MENUS.records.items.map(
      (item) => RECORDS[item.key as keyof typeof RECORDS].number
    );
    expect(numbers).toEqual([...numbers].sort());
    expect(numbers[0]).toBe('001');
  });
});

describe('the Author section (ADR: The sidebar, the addendum on About Steven as a page)', () => {
  it('is one link to the About page, no document in a window', () => {
    expect(MENUS.author.items).toEqual([]);
    expect(MENUS.author.links).toEqual([{ label: 'About Steven', href: '/?view=about' }]);
  });
});

describe('the rail names the project and the person plainly, and the resume has one address', () => {
  it('calls the project About TheYard and the person About Steven; the addresses do not change', () => {
    expect(MENUS.about.label).toBe('About TheYard');
    expect(MENUS.author.label).toBe('About Steven');
    expect(MENUS.about.items.map((item) => item.key)).toEqual(['readme', 'aiDevelopment']);
  });

  it('points every resume link the app writes at /resume?pdf=1 (ADR-099, the addendum on one address)', () => {
    expect(RESUME_ADDRESS).toBe('/resume?pdf=1');
    expect(LINKS.resume.href).toBe(RESUME_ADDRESS);
  });
});
