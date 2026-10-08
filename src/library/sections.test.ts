import { describe, expect, it } from 'vitest';
import { RECORDS } from './records';
import { MENUS } from './sections';

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
