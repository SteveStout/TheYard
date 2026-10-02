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
});
